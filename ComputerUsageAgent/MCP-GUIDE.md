## MCP Guide for ComputerUsageAgent

This guide explains how the `ComputerUsageAgent` sample connects to the Event Log MCP server and how to verify the integration end to end.

### Prerequisites

- `dotnet` 10 SDK (preview) installed and on the `PATH`
- Windows Event Log data available on the local machine
- Optional: administrator rights for fuller access to the System log (the agent falls back to the Application log automatically)

### Start the EventLog MCP server

1. Open a terminal in the repository root.
2. Run `dotnet run --project EventLogMcp`.
3. Leave the process running. The server listens at `http://localhost:5115/mcp` and logs HTTP requests to standard error.

### Run the ComputerUsageAgent

1. Open a second terminal in the repository root.
2. Execute `dotnet run --project ComputerUsageAgent`.
3. The agent connects to the MCP endpoint, lists the available tools, and prompts you to choose an analysis window (7 days, 30 days, or a custom range).
4. Select an option to trigger tool calls. The agent will invoke the MCP tools in sequence and render a textual summary plus a simple console graph.

### Verifying the connection

- Successful tool discovery appears in the console: `✓ Connected to MCP server with 3 tools available` followed by the tool names.
- Each analysis request results in HTTP activity in the MCP server console along with serialized JSON responses.
- If the agent reports it cannot reach the server, ensure the MCP process is running and listening on `http://localhost:5115/mcp` with no firewall blocks.

### Troubleshooting tips

- If zero events are returned, confirm that the requested date range contains Event Log entries and try running the MCP server with elevated privileges.
- When the MCP server fails to start, verify that no other process is bound to port 5115 or update the port in `EventLogMcp/Program.cs` and restart both processes.
- Authentication errors from Azure OpenAI indicate that `az login` or equivalent credential setup is required before running the agent.
