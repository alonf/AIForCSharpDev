# AI for C# Developers

Demonstrations of **Microsoft Agent Framework (MAF)** with practical C# examples.

## Projects

- HelloAgent: Basic agent with Azure OpenAI
- ComputerUsageAgent: Agent + MCP (native C#) for Windows Event Log analysis
- EventLogMcp: ASP.NET Core MCP server exposing Event Log tools

## Run

1. Start MCP server:
   - `cd EventLogMcp`
   - `dotnet run` (listening at `http://localhost:5115/mcp`)
2. Run agent:
   - `cd ComputerUsageAgent`
   - `dotnet run`

## MCP Tools

- `get_startup_shutdown_events`
- `calculate_uptime`
- `get_usage_summary`

## Prerequisites

- .NET 10 SDK
- Azure CLI (`az login`)