using System.ComponentModel;
using System.Text.Json;
using EventLogMcp.Services;
using ModelContextProtocol.Server;

namespace EventLogMcp.Tools;

/// <summary>
/// MCP Tool class exposing Windows Event Log analysis as MCP tools.
/// </summary>
[McpServerToolType]
public class EventLogTools
{
    private readonly WindowsEventLogReader _reader;

    public EventLogTools(WindowsEventLogReader reader)
    {
        _reader = reader;
    }

    [McpServerTool, Description("Retrieves system startup and shutdown events from Windows Event Log")]
    public async Task<string> GetStartupShutdownEvents(
        [Description("Number of days to look back (1-365)")] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var events = await _reader.GetStartupShutdownEventsAsync(days);
        return JsonSerializer.Serialize(events, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    [McpServerTool, Description("Calculates uptime statistics (total, average per day, daily breakdown)")]
    public async Task<string> CalculateUptime(
        [Description("Number of days to analyze (1-365)")] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var events = await _reader.GetStartupShutdownEventsAsync(days);
        var stats = _reader.CalculateUptime(events);
        return JsonSerializer.Serialize(stats, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    [McpServerTool, Description("Provides a human-readable usage summary for the period")]
    public async Task<string> GetUsageSummary(
        [Description("Number of days to analyze (1-365)")] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var events = await _reader.GetStartupShutdownEventsAsync(days);
        var statistics = _reader.CalculateUptime(events);

        var summary = new
        {
            AnalysisPeriod = $"Last {days} days",
            Statistics = new
            {
                TotalUptimeHours = statistics.TotalUptimeHours,
                AverageDailyHours = statistics.AverageDailyUptimeHours,
                DaysAnalyzed = statistics.DaysWithData,
                StartupCount = statistics.StartupCount,
                ShutdownCount = statistics.ShutdownCount
            },
            DailyBreakdown = statistics.DailyBreakdown.Select(d => new
            {
                Date = d.Date.ToString("yyyy-MM-dd"),
                UptimeHours = d.UptimeHours,
                EventCount = d.Events.Count,
                Events = d.Events.Select(e => new { Time = e.Timestamp.ToString("HH:mm:ss"), Type = e.EventType })
            })
        };

        return JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
