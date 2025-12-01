# Model Selection Guide for LocalOllamaAgent

## Why Model Selection Matters

Not all LLMs support **function/tool calling** - a critical feature for agentic systems. Without it, agents cannot:
- Call compilation tools
- Execute code
- Parse structured responses
- Follow multi-step workflows

## Current Model: llama3.1:8b ✅

**Selected because:**
- ✅ Consistently calls tools in this demo
- ✅ Strong instruction following for workflows
- ✅ Reasonable size (~4.9GB)
- ✅ Works with Microsoft Agent Framework (MAF)

## Models Tested

| Model | Tool Support | Size | Code Quality | Works? |
|-------|-------------|------|--------------|--------|
| `llama3.1:8b` | ✅ Excellent (default) | 4.9GB | Excellent | ✅ Yes |
| `qwen2.5-coder:7b` | ✅ Excellent | 4.7GB | Excellent | ✅ Yes |
| `llama3.3:latest` | ⚠️ Limited | 42GB | Excellent | ?? Untested |
| `mistral:latest` | ⚠️ Good | 4.1GB | Good | ?? Untested |
| `deepseek-coder:6.7b` | ⚠️ Good | 3.8GB | Excellent | ?? Untested |

## How to Change Model

Set the `OLLAMA_MODEL` environment variable before running:

```powershell
$env:OLLAMA_MODEL = "mistral:latest"   # PowerShell
```

or

```bash
export OLLAMA_MODEL="mistral:latest"
```

If unset, the demo defaults to `llama3.1:8b`. When you run the application it will:
1. Pull the new model (if not cached)
2. Use it for all agent interactions

## Recommended Alternatives

### If llama3.1:8b doesn't work:

1. **`mistral:latest`** (4.1GB)
   - Smaller than qwen
   - Good general-purpose model
   - Decent tool support

2. **`deepseek-coder:6.7b`** (3.8GB)
   - Code-specialized
   - Smaller than qwen
   - Good for coding tasks

3. **`llama3.3:latest`** (42GB)
   - Newest Llama model
   - Best reasoning
   - Large download (42GB!)

### For Production:

**Use Azure OpenAI or OpenAI API**
- Guaranteed tool calling support
- No local GPU needed
- Better performance
- Example: See `HelloAgent`, `JokeAgentsDemo`, `ComputerUsageAgent` projects

## Tool Calling Requirements

A model MUST support these to work with this demo:

1. **Function/Tool Calling** - Invoke functions with parameters
2. **Structured Output** - Parse and generate JSON
3. **Multi-turn Conversations** - Remember context
4. **Instruction Following** - Follow system prompts

## Signs Your Model Doesn't Support Tools

? Agent outputs JSON as text instead of calling function
? Agent invents fake results like `/path/to/file.dll`
? Agent says "I'll call the tool" but doesn't
? Agent repeats the same code without compiling
? Workflow gets stuck in loops

## Signs Your Model DOES Support Tools

? You see actual DLL paths like `C:\Users\...\Temp\Compile_xxx\...`
? Compilation errors show real dotnet output
? Program output is displayed correctly
? Workflow completes successfully
? Agent behavior is deterministic

## Performance Comparison

### llama3.1:8b (Current)
- **Startup:** 2-5 seconds (cached)
- **Response Time:** 2-6 seconds per turn
- **GPU Memory:** ~4GB VRAM
- **Quality:** Excellent for coding + tool usage

### qwen2.5-coder:7b (Alternative)
- **Startup:** 2-5 seconds (cached)
- **Response Time:** 3-8 seconds per turn
- **GPU Memory:** ~4GB VRAM
- **Quality:** Excellent for coding

## Troubleshooting

### Model won't download
```bash
docker exec ollama ollama pull qwen2.5-coder:7b
```

### Out of GPU memory
- Try smaller model: `mistral:latest` (4.1GB)
- Or: `deepseek-coder:1.3b` (1.3GB, limited capabilities)

### Model is slow
- Use GPU if available
- Reduce context window (not configurable in this demo)
- Switch to cloud API (Azure OpenAI)

### Still not calling tools
- Check console output for actual function invocations
- Look at CHANGES.md for debugging tips
- Consider switching to Azure OpenAI (guaranteed to work)

## Cloud Alternatives

If local models don't work or are too slow:

### Azure OpenAI (Recommended)
```csharp
var endpoint = new Uri("https://your-resource.openai.azure.com/");
var credential = new DefaultAzureCredential();
IChatClient client = new AzureOpenAIClient(endpoint, credential)
    .GetChatClient("gpt-4");
```

### OpenAI API
```csharp
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
IChatClient client = new OpenAIClient(apiKey)
    .GetChatClient("gpt-4");
```

### GitHub Models (Free Tier)
```csharp
var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
IChatClient client = new GitHubClient(token)
    .GetChatClient("gpt-4o");
```

## References

- [Ollama Models](https://ollama.com/library)
- [Microsoft Agent Framework](https://github.com/microsoft/agents)
- [OllamaSharp](https://github.com/awaescher/OllamaSharp)
- [Qwen 2.5 Coder](https://ollama.com/library/qwen2.5-coder)
