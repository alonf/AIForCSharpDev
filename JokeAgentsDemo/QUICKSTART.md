# ?? Quick Start - Joke Agents Demo

Get the demo running in **under 5 minutes**.

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
?? Joke Agents Demo - Microsoft Agent Framework A2A
====================================================

? Using Official MAF A2A APIs:
   • MapA2A() for agent hosting
   • A2AClient for remote agent discovery
   • GetAIAgent() for agent proxy creation

?? Application: http://localhost:5000

?? Agent Endpoints (A2A Protocol):
   JokeCreator: http://localhost:5000/agents/creator
   JokeCritic:  http://localhost:5000/agents/critic

?? Open http://localhost:5000 in your browser!
====================================================
```

---

## Step 4: Use the Demo

### Option A: Web UI
1. Open **http://localhost:5000** in browser
2. Enter a topic (e.g., "programming")
3. Click "Create Funny Joke"
4. Watch agents collaborate!

### Option B: API Call
```bash
curl -X POST "http://localhost:5000/api/jokes/create?topic=cats" \
  -H "Content-Type: application/json"
```

### Option C: HTTP File
Open `JokeAgentsDemo.http` in VS Code and click "Send Request"

---

## What You'll See

1. **Console Logs**: Real-time A2A communication
2. **Iteration Process**: JokeCreator ? A2AClient ? JokeCritic
3. **Final Result**: Joke with rating 8+ and iteration history

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
- ?? Watch console logs to see A2A calls
- ?? Inspect Agent Cards: http://localhost:5000/agents/critic/.well-known/agent.json
- ?? Deploy to Azure (see README)

---

**That's it! You're running a real MAF A2A multi-agent system!** ??
