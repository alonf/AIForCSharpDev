using System.Diagnostics.Eventing.Reader;
using EventLogMcp.Models;

namespace EventLogMcp.Services;

/// <summary>
/// Service for reading Windows Event Logs
/// </summary>
public class WindowsEventLogReader
{
    /// <summary>
    /// Gets startup and shutdown events for the past N days.
    /// Tries the System log first and falls back to Application if empty or access is denied.
    /// </summary>
    public async Task<List<StartupShutdownEvent>> GetStartupShutdownEventsAsync(int days)
    {
        return await Task.Run(() =>
        {
            var startDate = DateTime.Now.AddDays(-days);

            // Try System log
            var systemEvents = ReadEventsFromLogSafe("System", startDate);

            // If none or inaccessible, try Application log
            if (systemEvents.Count == 0)
            {
                var appEvents = ReadEventsFromLogSafe("Application", startDate);
                return appEvents.OrderBy(e => e.Timestamp).ToList();
            }

            return systemEvents.OrderBy(e => e.Timestamp).ToList();
        });
    }

    private List<StartupShutdownEvent> ReadEventsFromLogSafe(string logName, DateTime startLocal)
    {
        try
        {
            return ReadEventsFromLog(logName, startLocal);
        }
        catch
        {
            return new List<StartupShutdownEvent>();
        }
    }

    private List<StartupShutdownEvent> ReadEventsFromLog(string logName, DateTime startLocal)
    {
        var events = new List<StartupShutdownEvent>();

        // Convert to UTC for Event Log query
        var startUtc = startLocal.ToUniversalTime();

        // Event IDs for startup/shutdown
        var eventIds = new[] { 6005, 6006, 6008, 6009, 1074 };
        string query =
            $"*[System[(EventID={string.Join(" or EventID=", eventIds)}) and TimeCreated[@SystemTime>='{startUtc:yyyy-MM-ddTHH:mm:ss}.000Z']]]";

        var eventLogQuery = new EventLogQuery(logName, PathType.LogName, query);
        using var reader = new EventLogReader(eventLogQuery);

        EventRecord? eventRecord;
        while ((eventRecord = reader.ReadEvent()) != null)
        {
            using (eventRecord)
            {
                var eventType = eventRecord.Id switch
                {
                    6005 => "Startup",
                    6009 => "Startup",
                    6006 => "Shutdown",
                    6008 => "UnexpectedShutdown",
                    1074 => "Shutdown",
                    _ => "Unknown"
                };

                events.Add(new StartupShutdownEvent
                {
                    Timestamp = eventRecord.TimeCreated ?? DateTime.Now,
                    EventType = eventType,
                    EventId = eventRecord.Id,
                    Message = eventRecord.FormatDescription()
                });
            }
        }

        return events;
    }

    /// <summary>
    /// Calculates uptime statistics from startup/shutdown events.
    /// Distributes session time across days when sessions span midnight.
    /// </summary>
    public UptimeStatistics CalculateUptime(List<StartupShutdownEvent> events)
    {
        if (events == null || events.Count == 0)
        {
            return new UptimeStatistics
            {
                TotalUptimeHours = 0,
                AverageDailyUptimeHours = 0,
                DaysWithData = 0,
                StartupCount = 0,
                ShutdownCount = 0,
                DailyBreakdown = new List<DailyUptime>()
            };
        }

        var ordered = events.OrderBy(e => e.Timestamp).ToList();
        var daily = new Dictionary<DateTime, DailyUptime>();

        DateTime? sessionStart = null;
        foreach (var evt in ordered)
        {
            if (evt.EventType == "Startup")
            {
                sessionStart ??= evt.Timestamp;
            }
            else if ((evt.EventType == "Shutdown" || evt.EventType == "UnexpectedShutdown") && sessionStart.HasValue)
            {
                DistributeAcrossDays(sessionStart.Value, evt.Timestamp, daily, ordered);
                sessionStart = null;
            }
        }

        // Still running: count until now
        if (sessionStart.HasValue)
        {
            DistributeAcrossDays(sessionStart.Value, DateTime.Now, daily, ordered);
        }

        var totalUptime = daily.Values.Sum(d => d.UptimeHours);
        var daysWithData = daily.Count;
        var averageDaily = daysWithData > 0 ? totalUptime / daysWithData : 0;

        var startups = ordered.Count(e => e.EventType == "Startup");
        var shutdowns = ordered.Count(e => e.EventType == "Shutdown" || e.EventType == "UnexpectedShutdown");

        return new UptimeStatistics
        {
            TotalUptimeHours = Math.Round(totalUptime, 2),
            AverageDailyUptimeHours = Math.Round(averageDaily, 2),
            DaysWithData = daysWithData,
            StartupCount = startups,
            ShutdownCount = shutdowns,
            DailyBreakdown = daily.Values.OrderBy(d => d.Date).ToList()
        };
    }

    private void DistributeAcrossDays(DateTime start, DateTime end, Dictionary<DateTime, DailyUptime> daily, List<StartupShutdownEvent> allEvents)
    {
        if (end <= start) return;

        var current = start;
        while (current.Date < end.Date)
        {
            var endOfDay = current.Date.AddDays(1);
            AddToDay(current.Date, (endOfDay - current).TotalMinutes, daily, allEvents);
            current = endOfDay;
        }

        // Last segment within end date
        AddToDay(end.Date, (end - current).TotalMinutes, daily, allEvents);
    }

    private void AddToDay(DateTime day, double minutes, Dictionary<DateTime, DailyUptime> daily, List<StartupShutdownEvent> allEvents)
    {
        if (minutes <= 0) return;
        var hours = Math.Round(minutes / 60.0, 2);
        if (!daily.TryGetValue(day, out var du))
        {
            du = new DailyUptime { Date = day, UptimeHours = 0, Events = new List<StartupShutdownEvent>() };
            daily[day] = du;
        }
        du.UptimeHours += hours;
        du.Events = allEvents.Where(e => e.Timestamp.Date == day).ToList();
    }
}
