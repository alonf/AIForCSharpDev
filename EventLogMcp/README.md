# EventLogMcp

Native C# Model Context Protocol (MCP) server that exposes Windows Event Log analytics as AI tools. The server is consumed by the `ComputerUsageAgent` sample but can be used by any MCP-aware client.

## Key Capabilities

- Pure .NET implementation that runs as a single ASP.NET Core process
- HTTP MCP endpoint at `http://localhost:5115/mcp`
- Three Windows Event Log tools for startups, shutdowns, and uptime summaries
- Automatic fallback from the System log to the Application log when admin rights are unavailable
- JSON responses with camel-cased properties that are ready for AI agent consumption

## Architecture

```
ComputerUsageAgent (MAF)
                    |
                    |  HTTP MCP (localhost:5115/mcp)
                    v
EventLogMcp (ASP.NET Core + ModelContextProtocol)
                    |
                    v
System.Diagnostics.Eventing.Reader
                    |
                    v
Windows Event Log
```

## Available Tools

| Tool | Description | Typical Output |
|------|-------------|----------------|
| `get_startup_shutdown_events` | Returns startup, shutdown, and unexpected shutdown events for the requested period. | JSON array of events ordered by timestamp. |
| `calculate_uptime` | Calculates total uptime, average daily uptime, counts, and a day-by-day breakdown. | JSON object with aggregate statistics and per-day data. |
| `get_usage_summary` | Produces a compact, human-readable usage summary for the requested period. | JSON object with summary text, statistics, and daily breakdown. |

All tools clamp the `days` parameter to the range 1-365, serialize responses with indented JSON, and use camel-cased property names so that downstream agents can parse them consistently.

### Data Models

- `StartupShutdownEvent`: timestamp, event type, event ID, and optional message extracted from Windows Event Log.
- `UptimeStatistics`: total and average uptime, startup/shutdown counts, and a list of `DailyUptime` entries per day.

## Requirements

- Windows host with access to the local Event Log
- .NET 10 SDK (preview) because the project targets `net10.0-windows`
- Optional administrator rights for richer System log access (falls back to Application log otherwise)

## Running the Server

1. Restore dependencies: `dotnet restore`
2. Start the MCP HTTP server: `dotnet run --project EventLogMcp`
3. The server listens on `http://localhost:5115/mcp` and logs to standard error. Keep the process running while clients connect.

## Using with ComputerUsageAgent

1. In one terminal, start the MCP server as shown above.
2. In a second terminal, run `dotnet run --project ComputerUsageAgent`.
3. The agent connects to the MCP endpoint, lists the available tools, and offers prompts such as “Show computer usage for last 7 days.”

## Event IDs Captured

| Event ID | Source | Meaning |
|----------|--------|---------|
| 6005 | System | EventLog service started (startup) |
| 6006 | System | EventLog service stopped (shutdown) |
| 6009 | System | Operating system boot completed |
| 6008 | System | Unexpected shutdown detected |
| 1074 | System | Process or user initiated shutdown |

## Project Layout

```
EventLogMcp/
     EventLogMcp.csproj
     Program.cs                // Hosts the ASP.NET Core MCP server on localhost:5115
     Tools/
          EventLogTools.cs        // MCP tool implementations (three public tools)
     Services/
          EventLogReader.cs       // Windows Event Log queries and uptime calculations
     Models/
          EventLogModels.cs       // Records for events, uptime stats, and per-day data
     README.md
```

## Troubleshooting

- Ensure the process is running on the same machine as the client; the server is not exposed externally by default.
- If the client reports zero events, verify that the Event Log contains entries within the requested date range and that the process has privileges to read them.
- When running without administrator rights, expect data sourced from the Application log only, which may omit some shutdown events.

## License

Licensed under the MIT License. See `LICENSE.txt` in the repository root for details.
