# ?? Quick Start - Joke Agents Demo

Get the **MAF Group Chat Workflow** demo running in **under 5 minutes**.

---

## Prerequisites

- ? .NET 10 SDK
- ? Azure OpenAI access
- ? Azure CLI authenticated (`az login`)

---

## Step 1: Clone & Navigate

```bash
git clone https://github.com/alonf/AIForCSharpDev
cd AIForCSharpDev/JokeAgentsDemo
```

---

## Step 2: Configure Azure OpenAI

Edit `Program.cs` line 16-18 with your Azure OpenAI details:

```csharp
var endpoint = new Uri("https://YOUR-RESOURCE.cognitiveservices.azure.com/");
var credential = new DefaultAzureCredential();
string deploymentName = "YOUR-DEPLOYMENT-NAME";
```

---

## Step 3: Run

```bash
dotnet run
```

You should see:
```
====================================================
?? Joke Agents Demo - MAF Group Chat Workflow
====================================================

? Using MAF Group Chat Orchestration:
   • AgentWorkflowBuilder for workflow construction
   • RoundRobinGroupChatManager for coordination
   • Custom quality gate (ShouldTerminateAsync)
   • Automatic conversation history management

?? Application: http://localhost:5000

?? Agent Endpoints:
   JokeCreator: http://localhost:5000/agents/creator
   JokeCritic:  http://localhost:5000/agents/critic

?? Open http://localhost:5000 in your browser!
====================================================
```

---

## Step 4: Use the Demo

### Option A: ?? Streaming Web UI (Recommended!)
1. Open **http://localhost:5000** in browser
2. Click **"Launch Streaming Demo"**
3. Enter a topic (e.g., "programming")
4. Watch agents collaborate **in real-time**!
   - See text appear word-by-word
   - Live agent indicators
   - Progress tracking
   - ChatGPT-style experience

### Option B: ?? Classic Web UI
1. Open **http://localhost:5000** in browser
2. Click **"Launch Classic Demo"**
3. Enter a topic
4. Get complete results all at once

### Option C: API Call
```bash
# Streaming endpoint (SSE)
curl -N "http://localhost:5000/api/jokes/stream?topic=cats"

# Traditional endpoint
curl -X POST "http://localhost:5000/api/jokes/create?topic=cats" \
  -H "Content-Type: application/json"
```

### Option D: HTTP File
Open `JokeAgentsDemo.http` in VS Code and click "Send Request"

---

## What You'll See

### Streaming View ??
1. **Real-time collaboration**: Watch text appear as agents type
2. **Live status updates**: "JokeCreator is thinking..."
3. **Progress tracking**: Iteration counter updates live
4. **Instant feedback**: See ratings appear immediately
5. **Modern UX**: ChatGPT-style interface

### Classic View ??
1. **Console Logs**: Real-time agent conversation turns
2. **Group Chat Process**: Creator ? Critic ? Creator ? Critic...
3. **Quality Gate**: Workflow terminates when rating ? 8
4. **Final Result**: Joke with rating and iteration history

---

## Key Concepts

### MAF Group Chat Workflow

The demo uses **MAF Group Chat orchestration** with:

```csharp
// Custom manager with quality gate
public class JokeQualityManager : RoundRobinGroupChatManager
{
    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, ...)
    {
        // Terminate when critic approves (rating ? 8)
        var lastMessage = history.LastOrDefault();
        var rating = ExtractRating(lastMessage.Text);
        return ValueTask.FromResult(rating >= 8);
    }
}

// Build the workflow
var workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => new JokeQualityManager(agents, logger))
    .AddParticipants(creatorAgent, criticAgent)
    .Build();
```

**Key Features**:
- ? Automatic turn-taking between agents
- ? Shared conversation history
- ? Custom termination logic (quality gate)
- ? No manual context management needed

---

## Troubleshooting

### Error: DefaultAzureCredential failed
```bash
az login
```

### Error: Model not found
Check your deployment name matches the one in Azure OpenAI Studio

### Port 5000 in use
Change port in `launchSettings.json` or:
```bash
dotnet run --urls="http://localhost:5001"
```

---

## Next Steps

- ?? Read full [README.md](README.md)
- ?? Try different topics
- ?? Watch console logs to see agent turns
- ?? Inspect Agent Cards: http://localhost:5000/agents/critic/.well-known/agent.json
- ?? Learn about [MAF Group Chat](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/group-chat)

---

**That's it! You're running a MAF Group Chat workflow with quality gates!** ??
