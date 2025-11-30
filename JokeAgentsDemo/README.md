# ?? Joke Agents Demo - MAF Group Chat Workflow

A demonstration of **MAF Group Chat Orchestration** for iterative agent collaboration with quality gates.

## ? What This Demonstrates

- **MAF Group Chat Workflow**: Using `AgentWorkflowBuilder` and `RoundRobinGroupChatManager`
- **Custom Quality Gate**: `ShouldTerminateAsync()` for conditional workflow termination
- **Iterative Refinement**: Agents collaborating through managed conversation turns
- **Automatic Context Management**: Conversation history shared between agents
- **Agent Hosting**: A2A protocol with `MapA2A()` for distributed agents

---

## ??? Architecture

```
???????????????????????????????
?    Web UI (Browser)         ?
?  http://localhost:5000      ?
???????????????????????????????
           ? POST /api/jokes/create
           ?
???????????????????????????????
?   MAF Group Chat Workflow   ?
?   • AgentWorkflowBuilder    ?
?   • JokeQualityManager      ?
?   • ShouldTerminateAsync()  ?
???????????????????????????????
       ?              ?
       ?              ?
?????????????????? ??????????????????
? JokeCreator    ? ?  JokeCritic    ?
? (Participant)  ? ?  (Participant) ?
? • Creates      ? ?  • Evaluates   ?
? • Improves     ? ?  • Rates 1-10  ?
?????????????????? ??????????????????
       ?              ?
       ????????????????
              ?
    Shared Conversation History
    (Automatic Context Management)
```

---

## ?? MAF Group Chat Orchestration

### The Pattern

MAF Group Chat orchestration provides **managed iteration** with:
- ? **Turn-based conversation** between agents
- ? **Shared conversation history** (automatic context)
- ? **Custom termination logic** via `ShouldTerminateAsync()`
- ? **Centralized coordination** through manager
- ? **Quality gates** for conditional workflow termination

### Custom Quality Gate Manager

```csharp
public class JokeQualityManager : RoundRobinGroupChatManager
{
    public JokeQualityManager(IReadOnlyList<AIAgent> agents, ILogger logger) 
        : base(agents)
    {
        MaximumIterationCount = 5;  // Safety limit
    }

    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, 
        CancellationToken cancellationToken = default)
    {
        var lastMessage = history.LastOrDefault();
        
        // Check if critic approved the joke (rating ? 8)
        if (lastMessage?.AuthorName == "JokeCritic")
        {
            if (messageText.Contains("APPROVED"))
                return ValueTask.FromResult(true);  // ? Quality gate!
                
            var rating = ExtractRating(messageText);
            return ValueTask.FromResult(rating >= 8);
        }
        
        return ValueTask.FromResult(false);
    }
}
```

### Building the Workflow

```csharp
var workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => 
        new JokeQualityManager(agents, logger))
    .AddParticipants(creatorAgent, criticAgent)
    .Build();

// Execute the workflow
var messages = new List<ChatMessage> { 
    new(ChatRole.User, "Create a funny joke about programming") 
};

StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
```

---

## ?? Quick Start

### Prerequisites
- .NET 10 SDK
- Azure OpenAI access
- Azure CLI (`az login`)

### Run the Demo

```bash
cd JokeAgentsDemo
dotnet run
```

Open browser: **http://localhost:5000**

---

## ?? Key Features

### 1. **MAF Group Chat Workflow**

```csharp
// Build workflow with custom manager
var workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => 
        new JokeQualityManager(agents, logger))
    .AddParticipants(creatorAgent, criticAgent)
    .Build();
```

**Benefits**:
- ? Automatic conversation management
- ? Shared context between agents
- ? Declarative workflow definition
- ? Built-in iteration control

### 2. **Two Specialized Agents**

**JokeCreator Agent** ??
- Creates original, funny jokes
- Improves jokes based on conversation history
- Sees critic's feedback automatically

**JokeCritic Agent** ??
- Evaluates jokes on 6 criteria
- Provides detailed constructive feedback
- Signals termination with "APPROVED" or rating ? 8

### 3. **Automatic Iteration**

The Group Chat Manager:
1. Starts with initial user message
2. Alternates between Creator and Critic (round-robin)
3. Checks termination condition after each Critic response
4. Continues until rating ? 8 or max iterations reached

---

## ?? NuGet Packages

```xml
<PackageReference Include="Azure.AI.OpenAI" Version="2.1.0" />
<PackageReference Include="Azure.Identity" Version="1.13.1" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.0.0-preview.251125.1" />
<PackageReference Include="Microsoft.Agents.AI.A2A" Version="1.0.0-preview.251125.1" />
<PackageReference Include="Microsoft.Agents.AI.Hosting.A2A.AspNetCore" Version="1.0.0-preview.251125.1" />
<PackageReference Include="Microsoft.Agents.AI.Workflows.Declarative" Version="1.0.0-preview.251125.1" />
```

---

## ?? API Endpoints

### Agent Endpoints (Auto-generated by MapA2A)

- `POST /agents/critic` - JokeCritic invocation
- `GET /agents/critic/.well-known/agent.json` - Agent Card
- `POST /agents/creator` - JokeCreator invocation  
- `GET /agents/creator/.well-known/agent.json` - Agent Card

### Application Endpoints

- `GET /` - Web UI
- `POST /api/jokes/create?topic={topic}` - Create joke with Group Chat workflow
- `GET /health` - Health check

---

## ?? How Group Chat Works

### Workflow Execution Flow

```
User Request: "Create a joke about programming"
        ?
?????????????????????????????
?  Group Chat Manager       ?
?  • Manages turn-taking    ?
?  • Maintains history      ?
?  • Checks termination     ?
?????????????????????????????
           ?
    [Iteration 1]
    Creator ? "Here's a joke..."
    Critic  ? "Rating: 6/10, needs improvement..."
           ?
    [Iteration 2]
    Creator ? "Improved version..."
    Critic  ? "Rating: 8/10, APPROVED!"
           ?
    ? Workflow Terminates (Quality Gate Met)
```

### Key Components

1. **AgentWorkflowBuilder**: Creates the workflow
2. **RoundRobinGroupChatManager**: Coordinates agent turns
3. **ShouldTerminateAsync()**: Quality gate logic
4. **Conversation History**: Automatically shared context

---

## ?? For Lectures/Demos

### Demo Script (12 minutes)

**Minutes 0-2: Introduction**
- Show web UI at http://localhost:5000
- Explain MAF Group Chat orchestration pattern

**Minutes 2-4: Show Workflow Code**
- Open `Program.cs`
- Highlight `AgentWorkflowBuilder`
- Show `JokeQualityManager` with `ShouldTerminateAsync()`
- Explain quality gate logic

**Minutes 4-6: Explain Agent Collaboration**
- Two agents as Group Chat participants
- Round-robin turn-taking
- Automatic conversation history
- No manual context passing needed

**Minutes 6-10: Live Demo**
- Create a joke about "programming"
- Watch console logs showing agent turns
- Show iteration process
- Highlight quality gate termination

**Minutes 10-12: Q&A**
- When to use Group Chat vs. other patterns
- Benefits of workflow orchestration
- Production deployment considerations

---

## ?? Production Deployment

Group Chat workflows can be deployed as:
- ? Single service with both agents
- ? Distributed agents with A2A protocol
- ? Scalable workflow execution

```bash
# Deploy as single service
az webapp create --name joke-workflow-prod \
  --resource-group rg-agents \
  --runtime "DOTNET|10.0"
```

---

## ? Benefits of MAF Group Chat

| Feature | Manual Orchestration | MAF Group Chat |
|---------|---------------------|----------------|
| **Iteration Control** | ?? Manual loops | ? Automatic |
| **Context Management** | ?? Manual passing | ? Automatic history |
| **Code Complexity** | ?? More code | ? Declarative |
| **Quality Gates** | ? Custom logic | ? `ShouldTerminateAsync()` |
| **Maintainability** | ?? More boilerplate | ? Framework managed |
| **Learning Curve** | ? Explicit | ?? Framework concepts |

---

## ?? Learn More

- [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/)
- [Group Chat Orchestration](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/group-chat)
- [MAF Workflows](https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/)
- [GitHub Repository](https://github.com/microsoft/agent-framework)

---

## ?? Summary

This project demonstrates:
- ? **MAF Group Chat Workflow** - Managed iterative refinement
- ? **Custom Quality Gate** - Conditional termination logic
- ? **Automatic Context** - Conversation history management
- ? **Declarative Pattern** - Clean, maintainable code
- ? **Production Ready** - Framework-managed orchestration

**Perfect for teaching MAF workflows and collaborative agent patterns!** ????
