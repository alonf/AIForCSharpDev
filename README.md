# AIForCSharpDev — Demos Index

A collection of C# demos showcasing practical AI agent patterns, including group-chat orchestration, Model Context Protocol (MCP) integration, and Windows Event Log analytics.

## Prerequisites

- .NET 10 SDK (preview)
- Windows host for Windows Event Log–based samples
- Optional administrator rights for richer System log access in Event Log–based samples
- Azure OpenAI access (required for JokeAgentsDemo)
- Additional project-specific requirements are noted below and in each project’s README

## Getting Started

- Open the solution: `AIForCSharpDev.slnx`
- Or run projects individually with `dotnet run --project <ProjectName>`

## Demos

### 1) HelloAgent
- Path: [HelloAgent/](./HelloAgent)
- Summary: Simple “hello world” agent sample to get started quickly.
- Setup/Run: See project directory for details.

---

### 2) JokeAgentsDemo — Two-Agent Group Chat with Streaming UI
- Path: [JokeAgentsDemo/](./JokeAgentsDemo)
- Summary:
  - Demonstrates a two-agent “creator and critic” workflow using the Microsoft Agent Framework (MAF)
  - Group chat orchestration with a custom quality gate that stops when the critic approves or rates highly
  - Browser UI with both streaming (SSE) and classic result views
  - REST and SSE endpoints for automation and integrations
- Prerequisites:
  - .NET 10 SDK (preview)
  - Azure OpenAI with a deployed chat-completion model
  - Local Azure authentication (`az login`)
- Configure:
  - Update Azure OpenAI settings in `Program.cs`:
    ```csharp
    var endpoint = new Uri("https://<your-resource>.cognitiveservices.azure.com/");
    var credential = new DefaultAzureCredential();
    string deploymentName = "<your-deployment-name>";
    ```
- Run:
  ```bash
  dotnet run --project JokeAgentsDemo
  ```
  Open http://localhost:5000 and choose the streaming or classic experience.
- Endpoints:
  - `GET /` — Web UI
  - `POST /api/jokes/create?topic=<topic>` — One-shot workflow
  - `GET /api/jokes/stream?topic=<topic>` — Streaming via SSE
  - `POST /agents/creator` — Direct A2A to creator
  - `POST /agents/critic` — Direct A2A to critic
  - `GET /agents/{agent}/.well-known/agent.json` — Agent cards
  - `GET /health` — Health check
- More: See the project’s README at [JokeAgentsDemo/README.md](./JokeAgentsDemo/README.md)

---

### 3) EventLogMcp — Windows Event Log MCP Server
- Path: [EventLogMcp/](./EventLogMcp)
- Summary:
  - Native C# MCP server exposing Windows Event Log analytics as AI tools
  - HTTP MCP endpoint at `http://localhost:5115/mcp`
  - Tools:
    - `get_startup_shutdown_events` — Startup, shutdown, and unexpected shutdown events for a period
    - `calculate_uptime` — Aggregate and per-day uptime statistics
    - `get_usage_summary` — Compact, human-readable usage summary with stats
  - Automatic fallback from System log to Application log when admin rights are unavailable
  - JSON responses with camel-cased properties suitable for agents
- Requirements:
  - Windows host with access to the local Event Log
  - .NET 10 SDK (preview), targets `net10.0-windows`
  - Optional admin rights (for System log)
- Run:
  ```bash
  dotnet run --project EventLogMcp
  ```
  The server listens on `http://localhost:5115/mcp`.
- More: See [EventLogMcp/README.md](./EventLogMcp/README.md)

---

### 4) ComputerUsageAgent — Agent Using EventLogMcp
- Path: [ComputerUsageAgent/](./ComputerUsageAgent)
- Summary:
  - Agent that connects to the MCP server from `EventLogMcp` to analyze Windows computer usage via Event Logs
- Run:
  1) Start the MCP server:
     ```bash
     cd EventLogMcp
     dotnet run
     ```
  2) Run the agent:
     ```bash
     cd ../ComputerUsageAgent
     dotnet run
     ```
- More: See [ComputerUsageAgent/README.md](./ComputerUsageAgent/README.md)

---

### 5) LocalOllamaAgent
- Path: [LocalOllamaAgent/](./LocalOllamaAgent)
- Summary: Local model agent demo. See the project directory for setup and usage details.

## Repository Structure (top-level)

```
AIForCSharpDev.slnx
ComputerUsageAgent/
EventLogMcp/
HelloAgent/
JokeAgentsDemo/
LocalOllamaAgent/
Setup/
LICENSE.txt
CHANGES.md
PROJECT-STRUCTURE.md
TEST_PLAN.md
```

## License

MIT — see [LICENSE.txt](./LICENSE.txt)
