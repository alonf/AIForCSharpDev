# Quick Start - Computer Usage Agent (C# MCP)

## Prerequisites
- .NET 10 SDK
- Azure CLI authenticated (`az login`)

## Run
1. Start the MCP server:
   - `cd ../EventLogMcp`
   - `dotnet run` (listening on `http://localhost:5115/mcp`)
2. Run the agent:
   - `cd .`
   - `dotnet run`

## What You'll See
- Agent connects to MCP and lists tools:
  - `get_startup_shutdown_events`
  - `calculate_uptime`
  - `get_usage_summary`
- You choose a period (7/30/custom days)
- Text results plus a console bar chart for daily usage

## Troubleshooting
- Ensure MCP server is running and reachable: `http://localhost:5115/mcp`
- Run `az login` to enable `DefaultAzureCredential`

## Notes
- No Python/Poetry required.
- Admin is not required; the server gracefully falls back to Application log if System log is inaccessible.
