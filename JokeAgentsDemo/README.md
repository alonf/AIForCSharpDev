dotnet run
# Joke Agents Demo

Demonstration of the Microsoft Agent Framework (MAF) group chat workflow applied to a two-agent joke writing scenario. The sample showcases iterative collaboration, quality-gated termination, and a real-time streaming web experience.

## Highlights

- Group chat workflow built with `AgentWorkflowBuilder` and `RoundRobinGroupChatManager`
- Custom `JokeQualityManager` that stops the conversation once the critic delivers an approval or high rating
- Two specialized agents (`JokeCreator` and `JokeCritic`) exposed through the A2A protocol
- Browser UI with both streaming and classic result views powered by Server-Sent Events
- REST and SSE endpoints for integrating with other clients or demos

## Prerequisites

- .NET 10 SDK (preview)
- Azure OpenAI access with a deployed chat-completion model
- Azure CLI authenticated locally (`az login`)

## Configure and Run

1. Update the Azure OpenAI settings in `Program.cs`:
   ```csharp
   var endpoint = new Uri("https://<your-resource>.cognitiveservices.azure.com/");
   var credential = new DefaultAzureCredential();
   string deploymentName = "<your-deployment-name>";
   ```
2. Restore and run the project:
   ```bash
   dotnet run --project JokeAgentsDemo
   ```
3. Open `http://localhost:5000` in a browser and choose either the streaming or classic experience.

### What to Expect

- Streaming view: live, token-by-token responses, agent status indicators, and iteration progress.
- Classic view: displays the full conversation once the workflow completes.
- Console output: turn-by-turn transcript and quality gate evaluations.

## Architecture Overview

```
Browser UI (Streaming or Classic)
        |
        |  HTTP (REST + SSE)
        v
ASP.NET Core host (Program.cs)
        |
        |  MAF Group Chat Workflow
        v
RoundRobinGroupChatManager (JokeQualityManager)
    |                |
    v                v
JokeCreator agent   JokeCritic agent

Conversation history is shared automatically between agents.
```

## Core Workflow Logic

```csharp
public class JokeQualityManager : RoundRobinGroupChatManager
{
    public JokeQualityManager(IReadOnlyList<AIAgent> agents, ILogger logger)
        : base(agents)
    {
        MaximumIterationCount = 5;
    }

    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = history.LastOrDefault();
        if (lastMessage?.AuthorName != "JokeCritic")
        {
            return ValueTask.FromResult(false);
        }

        var messageText = lastMessage.Text ?? string.Empty;
        if (messageText.Contains("APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(true);
        }

        var rating = ExtractRating(messageText);
        return ValueTask.FromResult(rating >= 8);
    }
}
```

The workflow alternates turns between the creator and critic, keeping a shared conversation history so each agent can build on the other's output. The manager checks for the critic's approval after every turn and stops the conversation when the quality gate is met or the iteration cap is reached.

## Agents and Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /` | Serves the web UI (streaming and classic options). |
| `POST /api/jokes/create?topic=<topic>` | Runs the workflow once and returns the final result. |
| `GET /api/jokes/stream?topic=<topic>` | Streams workflow updates via SSE. |
| `POST /agents/creator` | Direct A2A invocation of the creator agent. |
| `POST /agents/critic` | Direct A2A invocation of the critic agent. |
| `GET /agents/{agent}/.well-known/agent.json` | Agent cards for discovery. |
| `GET /health` | Basic health check. |

## Demo Flow (12 minutes)

1. **Overview (2 min)** – Show the UI, explain the group chat concept.
2. **Code Tour (2 min)** – Highlight `Program.cs`, `JokeQualityManager`, and the agent prompts.
3. **Workflow Walkthrough (2 min)** – Describe the turn-taking and quality gate logic.
4. **Live Run (4 min)** – Launch the streaming view, enter a topic, and narrate the iterations.
5. **Wrap-up (2 min)** – Discuss when to use group chat orchestration and answer questions.

## Troubleshooting

- **Authentication errors**: Run `az login` to refresh Azure credentials.
- **Model not found**: Ensure the deployment name in `Program.cs` matches an existing Azure OpenAI deployment.
- **Port already in use**: Start with a different port, for example `dotnet run --project JokeAgentsDemo --urls http://localhost:5001`.
- **No approval from critic**: Check the agent prompts and confirm both agents have access to the full conversation history.

## Project Structure

```
JokeAgentsDemo/
  Program.cs
  JokeWorkflowAgentFactory.cs
  JokeQualityManager.cs
  JokeQualityManager.cs.bak (archived reference)
  JokeQualityManager.csproj
  wwwroot/
    index.html
  README.md
```

## Learning Resources

- Microsoft Agent Framework documentation: https://learn.microsoft.com/agent-framework/
- Group chat orchestration guide: https://learn.microsoft.com/agent-framework/user-guide/workflows/orchestrations/group-chat
- Workflow tutorials: https://learn.microsoft.com/agent-framework/tutorials/workflows/

Use this demo to illustrate collaborative agent patterns, streaming UX, and quality-gated execution within the Microsoft Agent Framework.
