using System.Globalization;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace ComputerUsageAgent.Visualization;

public static class ConsoleGraphRenderer
{
    // Extract daily usage (date -> hours) from the agent text and render as a bar chart
    public static void RenderFromText(string text, int maxBars = 365)
    {
        var data = ExtractDailyUsage(text).ToList();
        if (data.Count == 0)
            return;

        // Sort by date ascending to align with text summary and ensure consistent order
        data.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.Ordinal));

        // Limit bars if too many
        if (data.Count > maxBars)
        {
            data = data.TakeLast(maxBars).ToList();
        }

        var chart = new BarChart()
            .Width(Math.Min(120, Console.WindowWidth > 0 ? Console.WindowWidth - 4 : 100))
            .Label("Daily Usage (hours)")
            .CenterLabel();

        foreach (var (label, hours) in data)
        {
            chart.AddItem(label, (float)hours, Color.Green);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]Bar chart:[/]");
        AnsiConsole.Write(chart);
        AnsiConsole.WriteLine();
    }

    private static IEnumerable<(string Label, double Hours)> ExtractDailyUsage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        // Accept lines like:
        // - 2025-11-10: 0.51 hours
        // 2025-11-10: 0.51 hours
        // 2025-11-10 - 0.51 hours
        // 2025-11-10 => 0.51 hrs
        var patterns = new[]
        {
            new Regex(@"(?m)^\s*[-•]?\s*(?<date>\d{4}-\d{2}-\d{2})\s*[:\-?=>]\s*(?<hours>\d+(?:[\.,]\d+)?)\s*(?:h|hr|hrs|hour|hours)?\b", RegexOptions.Compiled),
            new Regex(@"(?m)^\s*[-•]?\s*(?<date>\d{4}-\d{2}-\d{2}).{0,6}(?<hours>\d+(?:[\.,]\d+)?)\s*(?:h|hr|hrs|hour|hours)\b", RegexOptions.Compiled)
        };

        var seen = new HashSet<string>();
        foreach (var rx in patterns)
        {
            foreach (Match m in rx.Matches(text))
            {
                var date = m.Groups["date"].Value;
                var hoursRaw = m.Groups["hours"].Value.Replace(',', '.');
                if (!double.TryParse(hoursRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours))
                    continue;

                // Preserve exact hours as printed
                if (hours <= 0) continue;
                if (seen.Add(date))
                    yield return (date, hours);
            }
        }
    }
}
