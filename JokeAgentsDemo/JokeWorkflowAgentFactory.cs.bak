using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

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

    public JokeWorkflowChatClient(
        ChatClientAgent creatorAgent,
        ChatClientAgent criticAgent,
        ILogger logger)
    {
        _creatorAgent = creatorAgent;
        _criticAgent = criticAgent;
        _logger = logger;
    }

    public ChatClientMetadata Metadata => new("JokeWorkflow", new Uri("https://joke-workflow"));

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
        StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
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
        _logger.LogInformation("=== AG-UI: Executing Joke Workflow (streaming) ===");

        // Build workflow
        var workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
                new JokeQualityManager(agents, _logger))
            .AddParticipants(_creatorAgent, _criticAgent)
            .Build();

        // Execute workflow
        var messages = chatMessages.ToList();
        StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        // Stream workflow events
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            if (evt is AgentRunUpdateEvent update)
            {
                var response = update.AsResponse();
                foreach (var message in response.Messages)
                {
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        // Create update with text content
                        yield return new ChatResponseUpdate
                        {
                            Contents = [new TextContent(message.Text)],
                            Role = message.Role,
                            AuthorName = message.AuthorName
                        };
                    }
                }
            }
            else if (evt is WorkflowOutputEvent)
            {
                yield break;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
