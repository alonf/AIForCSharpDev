# Project Structure

This solution contains two demonstration projects for Microsoft Agent Framework (MAF).

## Solution: AIForCSharpDev.sln

### Projects

1. **HelloAgent** - Basic MAF introduction
2. **ComputerUsageAgent** - MAF with MCP integration

## Directory Structure

```
AIForCSharpDev/
??? AIForCSharpDev.sln              # Solution file
??? README.md                        # Main documentation
?
??? HelloAgent/                      # Project 1: Basic MAF
?   ??? HelloAgent.csproj
?   ??? Program.cs
?   ??? README.md
?
??? ComputerUsageAgent/              # Project 2: MAF + MCP
?   ??? ComputerUsageAgent.csproj
?   ??? Program.cs
?   ??? README.md                    # Main guide
?   ??? QUICKSTART.md                # Quick setup
?   ??? MCP-GUIDE.md                 # MCP details
?
??? Setup/                           # MCP server setup
    ??? setup-winlog-mcp.ps1
```

## Building the Solution

```powershell
# Build all projects
dotnet build

# Or build specific project
dotnet build HelloAgent
dotnet build ComputerUsageAgent
```

## Running Projects

### HelloAgent
```powershell
cd HelloAgent
dotnet run
```

### ComputerUsageAgent
```powershell
# Setup MCP (first time only)
cd Setup
.\setup-winlog-mcp.ps1

# Set environment
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT = "https://your-project.cognitiveservices.azure.com/"

# Run
cd ..\ComputerUsageAgent
dotnet run
```

## Technology Stack

- **.NET:** 10.0
- **Language:** C# 14
- **Framework:** Microsoft Agent Framework (MAF)
- **AI Provider:** Azure OpenAI / Azure AI Projects
- **Protocol:** Model Context Protocol (MCP)
- **UI:** Spectre.Console

## Key Files

| File | Purpose |
|------|---------|
| `AIForCSharpDev.sln` | Visual Studio solution |
| `README.md` | Main documentation |
| `HelloAgent/Program.cs` | Basic MAF example |
| `ComputerUsageAgent/Program.cs` | MAF + MCP example |
| `Setup/setup-winlog-mcp.ps1` | MCP server setup |

## Documentation

- **Root README.md** - Overview and quick start
- **HelloAgent/README.md** - Basic MAF guide
- **ComputerUsageAgent/README.md** - Full MAF + MCP guide
- **ComputerUsageAgent/QUICKSTART.md** - 5-minute setup
- **ComputerUsageAgent/MCP-GUIDE.md** - MCP protocol details

## Next Steps

1. Start with **HelloAgent** to learn MAF basics
2. Move to **ComputerUsageAgent** for MCP integration
3. Read the MCP-GUIDE for protocol details
4. Experiment with your own MCP servers

## Resources

- [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/)
- [Model Context Protocol](https://spec.modelcontextprotocol.io/)
- [Azure AI Projects](https://learn.microsoft.com/en-us/azure/ai-services/agents/)
