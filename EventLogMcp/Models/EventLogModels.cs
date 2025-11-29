namespace EventLogMcp.Models;

/// <summary>
/// Represents a system startup or shutdown event
/// </summary>
public record StartupShutdownEvent
{
    /// <summary>
    /// Timestamp when the event occurred
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Type of event (Startup, Shutdown, UnexpectedShutdown)
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Event ID from Windows Event Log
    /// </summary>
    public int EventId { get; init; }

    /// <summary>
    /// Additional message or details
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Statistics about system uptime
/// </summary>
public record UptimeStatistics
{
    /// <summary>
    /// Total hours the system was running
    /// </summary>
    public double TotalUptimeHours { get; init; }

    /// <summary>
    /// Average daily uptime in hours
    /// </summary>
    public double AverageDailyUptimeHours { get; init; }

    /// <summary>
    /// Number of days with data
    /// </summary>
    public int DaysWithData { get; init; }

    /// <summary>
    /// Number of startups recorded
    /// </summary>
    public int StartupCount { get; init; }

    /// <summary>
    /// Number of shutdowns recorded
    /// </summary>
    public int ShutdownCount { get; init; }

    /// <summary>
    /// Daily breakdown of uptime
    /// </summary>
    public List<DailyUptime> DailyBreakdown { get; set; } = new();
}

/// <summary>
/// Uptime for a specific day
/// </summary>
public record DailyUptime
{
    /// <summary>
    /// The date
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Hours of uptime on that day
    /// </summary>
    public double UptimeHours { get; set; }

    /// <summary>
    /// Events that occurred on that day
    /// </summary>
    public List<StartupShutdownEvent> Events { get; set; } = new();
}
