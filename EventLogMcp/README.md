# EventLogMcp - Native C# MCP Server

A **pure C# implementation** of an MCP (Model Context Protocol) server that exposes Windows Event Log analysis tools as AI functions.

## ?? Purpose

This project demonstrates how to create **native .NET MCP tools** without external dependencies (no Python, no Poetry, no stdio transport issues). It provides Windows Event Log analysis capabilities that can be consumed by AI agents through the Microsoft Agent Framework (MAF).

## ? Key Features

- ? **Pure C#** - No Python or external dependencies
- ? **In-Process** - No stdio communication, no hanging issues
- ? **No Admin Required** - Works with Application log (falls back gracefully)
- ? **AI Function Tools** - Uses `[Description]` attributes for automatic discovery
- ? **Fast & Reliable** - Native performance, single-process debugging

## ??? Architecture

```
AI Agent (MAF)
     ?
Microsoft.Extensions.AI
     ?
EventLogMcpServer (In-Process)
   - GetStartupShutdownEvents()
   - CalculateUptime()
   - GetUsageSummary()
     ?
System.Diagnostics.Eventing.Reader
     ?
Windows Event Log
```

## ?? Available Tools

### 1. GetStartupShutdownEvents
```csharp
public async Task<string> GetStartupShutdownEvents(int days = 30)
```
Retrieves system startup and shutdown events from Windows Event Log.

**Parameters:**
- `days` - Number of days to look back (1-365)

**Returns:** JSON list of events with timestamps and types

### 2. CalculateUptime
```csharp
public async Task<string> CalculateUptime(int days = 30)
```
Calculates system uptime statistics with daily breakdown.

**Parameters:**
- `days` - Number of days to analyze (1-365)

**Returns:** JSON statistics including:
- Total uptime hours
- Average daily uptime
- Startup/shutdown counts
- Day-by-day breakdown

### 3. GetUsageSummary
```csharp
public async Task<string> GetUsageSummary(int days = 30)
```
Provides human-readable summary of computer usage patterns.

**Parameters:**
- `days` - Number of days to analyze (1-365)

**Returns:** JSON formatted summary with daily events

## ?? Dependencies

```xml
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.0.0-preview.1.25228.1" />
<PackageReference Include="System.ComponentModel.Annotations" Version="6.0.0" />
```

## ?? Usage

### As a Library Reference

```csharp
// In your AI agent project
var mcpServer = new EventLogMcpServer();

// Convert methods to AI functions
var tools = AIFunctionFactory.Create(mcpServer);

// Pass to AI agent
AIAgent agent = client.CreateAIAgent(
    instructions: "You are a computer usage analyst...",
    tools: tools
);

// Agent can now call the tools automatically
var response = await agent.RunAsync("Show me my computer usage for the last week");
```

### Direct Usage

```csharp
var server = new EventLogMcpServer();

// Get events
string eventsJson = await server.GetStartupShutdownEvents(days: 7);

// Calculate uptime
string uptimeJson = await server.CalculateUptime(days: 7);

// Get summary
string summaryJson = await server.GetUsageSummary(days: 30);
```

## ?? Event IDs Captured

| Event ID | Source | Meaning |
|----------|--------|---------|
| 6005 | System | EventLog service started (startup) |
| 6006 | System | EventLog service stopped (shutdown) |
| 6009 | System | OS boot (startup) |
| 6008 | System | Unexpected shutdown |
| 1074 | System | Process/application initiated shutdown |

## ?? Privilege Handling

The server tries to read from the **System** event log first (requires admin), but automatically falls back to the **Application** log if access is denied. This ensures the tool works in both scenarios:

- ? **With Admin**: Full access to System log events
- ? **Without Admin**: Graceful fallback to Application log

## ?? Advantages Over Python MCP Servers

| Aspect | Python MCP | C# EventLogMcp |
|--------|------------|----------------|
| **Setup** | Python + Poetry + deps | NuGet restore only |
| **Communication** | stdio (can hang) | In-process (reliable) |
| **Debugging** | Multi-process | Single process |
| **Performance** | Process overhead | Native speed |
| **Dependencies** | Python runtime required | None |
| **Integration** | External process | Same AppDomain |
| **Maintenance** | Python + C# code | C# only |

## ?? Models

### StartupShutdownEvent
```csharp
public record StartupShutdownEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType { get; init; }  // "Startup", "Shutdown", "UnexpectedShutdown"
    public int EventId { get; init; }
    public string? Message { get; init; }
}
```

### UptimeStatistics
```csharp
public record UptimeStatistics
{
    public double TotalUptimeHours { get; init; }
    public double AverageDailyUptimeHours { get; init; }
    public int DaysWithData { get; init; }
    public int StartupCount { get; init; }
    public int ShutdownCount { get; init; }
    public List<DailyUptime> DailyBreakdown { get; init; }
}
```

## ??? Project Structure

```
EventLogMcp/
??? EventLogMcp.csproj
??? Class1.cs                      // MCP Server with [Description] decorated tools
??? Models/
?   ??? EventLogModels.cs          // Data models
??? Services/
?   ??? EventLogReader.cs          // Windows Event Log access
??? README.md
```

## ?? Integration with ComputerUsageAgent

This project is designed to be consumed by the `ComputerUsageAgent` project, replacing the external Python MCP server (EventWhisper/WinLog-MCP) with a native C# implementation.

**Before** (External Python MCP):
```
ComputerUsageAgent ? stdio ? Poetry ? Python ? EventWhisper ? Windows API
```

**After** (Native C# MCP):
```
ComputerUsageAgent ? EventLogMcp ? Windows API
```

## ?? License

MIT License - Educational demonstration project

## ?? Learning Objectives

This project demonstrates:
1. Creating **native C# MCP tools** without external dependencies
2. Using **`[Description]` attributes** for AI function metadata
3. **In-process tool invocation** avoiding stdio issues
4. **Windows Event Log** access via .NET APIs
5. **Graceful degradation** (admin vs non-admin scenarios)
6. Modern C# patterns (records, async/await, nullable references)

---

**Built with:** .NET 10 • C# 14 • Microsoft.Extensions.AI • Native MCP
