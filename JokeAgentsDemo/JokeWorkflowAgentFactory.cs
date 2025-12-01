using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace JokeAgentsDemo;

/// <summary>
/// AG-UI-compatible agent wrapper for the Joke Creation workflow.
/// This demonstrates that AG-UI is a protocol layer - the business logic
/// (our workflow) is independent of the communication mechanism.
/// </summary>
public static class JokeWorkflowAgentFactory
{
    public static ChatClientAgent Create(
        ChatClientAgent creatorAgent,
        ChatClientAgent criticAgent,
        ILogger logger)
    {
        // Create an IChatClient that wraps our workflow logic
        var workflowChatClient = new JokeWorkflowChatClient(
            creatorAgent,
            criticAgent,
            logger
        );

        // Wrap it in a ChatClientAgent for AG-UI compatibility
        return new ChatClientAgent(
            workflowChatClient,
            new ChatClientAgentOptions("Creates jokes using iterative group chat workflow")
            {
                Name = "JokeWorkflow",
                Description = "Joke creation workflow with quality gate (AG-UI enabled)"
            }
        );
    }
}

/// <summary>
/// IChatClient implementation that executes our workflow
/// </summary>
internal class JokeWorkflowChatClient : IChatClient
{
    private readonly ChatClientAgent _creatorAgent;
    private readonly ChatClientAgent _criticAgent;
    private readonly ILogger _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public JokeWorkflowChatClient(
        ChatClientAgent creatorAgent,
        ChatClientAgent criticAgent,
        ILogger logger)
    {
        _creatorAgent = creatorAgent;
        _criticAgent = criticAgent;
        _logger = logger;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== AG-UI: Executing Joke Workflow (non-streaming) ===");

        // Build workflow
        var workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
                new JokeQualityManager(agents, _logger))
            .AddParticipants(_creatorAgent, _criticAgent)
            .Build();

        // Execute workflow
        var messages = chatMessages.ToList();
        StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages, cancellationToken: cancellationToken);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        // Collect all results
        List<ChatMessage> results = new();
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            if (evt is AgentRunUpdateEvent update)
            {
                var response = update.AsResponse();
                results.AddRange(response.Messages);
            }
            else if (evt is WorkflowOutputEvent output)
            {
                var history = output.As<List<ChatMessage>>() ?? new List<ChatMessage>();
                results = history;
                break;
            }
        }

        // Return as ChatResponse
        var finalMessage = results.LastOrDefault() ?? new ChatMessage(ChatRole.Assistant, "Workflow completed");
        return new ChatResponse([finalMessage]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== AG-UI: Executing Joke Workflow (streaming passthrough + headers) ===");

        // Build workflow
        var workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
                new JokeQualityManager(agents, _logger))
            .AddParticipants(_creatorAgent, _criticAgent)
            .Build();

        // Execute workflow
        var messages = chatMessages.ToList();
        StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages, cancellationToken: cancellationToken);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        // Optional intro header (single chunk)
        yield return new ChatResponseUpdate
        {
            Contents = [new TextContent("\U0001F3AD Joke Creation Workflow - Live Conversation:\n\n")],
            Role = ChatRole.Assistant
        };

        string? currentAuthor = null;
        int iteration = 0;

        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            if (evt is AgentRunUpdateEvent update)
            {
                var response = update.AsResponse();
                var msg = response.Messages.LastOrDefault();
                var chunk = response.Text; // passthrough text chunk from workflow

                var author = msg?.AuthorName;
                if (!string.IsNullOrEmpty(author) && author != currentAuthor)
                {
                    if (author == "JokeCreator")
                    {
                        iteration++;
                        yield return new ChatResponseUpdate
                        {
                            Contents = [new TextContent($"\n\U0001F916 Iteration {iteration} - JokeCreator:\n")],
                            Role = ChatRole.Assistant
                        };
                    }
                    else if (author == "JokeCritic")
                    {
                        yield return new ChatResponseUpdate
                        {
                            Contents = [new TextContent($"\n\U0001F3AF Iteration {iteration} - JokeCritic:\n")],
                            Role = ChatRole.Assistant
                        };
                    }
                    currentAuthor = author;
                }

                if (!string.IsNullOrEmpty(chunk))
                {
                    // Pure passthrough of chunk
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new TextContent(chunk)],
                        Role = ChatRole.Assistant
                    };
                }
            }
            else if (evt is WorkflowOutputEvent output)
            {
                // Final summary with last critic rating and last creator joke
                var history = output.As<List<ChatMessage>>() ?? new List<ChatMessage>();
                var finalCreator = history.LastOrDefault(m => m.AuthorName == "JokeCreator" && m.Role == ChatRole.Assistant);
                var finalCritic = history.LastOrDefault(m => m.AuthorName == "JokeCritic" && m.Role == ChatRole.Assistant);

                int finalRating = 0;
                if (!string.IsNullOrEmpty(finalCritic?.Text))
                {
                    finalRating = JokeQualityManager.ExtractRating(finalCritic.Text);
                }

                if (!string.IsNullOrEmpty(finalCreator?.Text))
                {
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new TextContent($"\n\n---\n\n\U0001F389 FINAL APPROVED JOKE:\n{finalCreator!.Text}\n\n\u2705 Approved after {Math.Max(iteration, 1)} iteration(s)\n\u2B50 Final Rating: {finalRating}/10\n")],
                        Role = ChatRole.Assistant
                    };
                }

                yield break;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
