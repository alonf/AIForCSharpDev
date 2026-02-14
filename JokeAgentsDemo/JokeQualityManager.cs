using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace JokeAgentsDemo;

/// <summary>
/// Custom Group Chat Manager with quality gate for joke rating.
/// Follows the same explicit-check pattern as CodeWorkflowManager.
/// </summary>
public class JokeQualityManager : RoundRobinGroupChatManager
{
    private const int MaxRounds = 10;
    private readonly ILogger _logger;

    public JokeQualityManager(IReadOnlyList<AIAgent> agents, ILogger logger) 
        : base(agents)
    {
        _logger = logger;
        _logger.LogWarning("JokeQualityManager v4 loaded — MaxRounds={MaxRounds}, no base.ShouldTerminate call", MaxRounds);
    }

    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, 
        CancellationToken cancellationToken = default)
    {
        var lastMessage = history.LastOrDefault();
        var authorName = lastMessage?.AuthorName ?? "null";

        // Count actual rounds by counting critic responses (immune to message-count inflation)
        var criticCount = history.Count(m => m.AuthorName == "JokeCritic");

        _logger.LogInformation(
            "ShouldTerminate — {Count} messages, {CriticCount} critic responses, last author: {Author}",
            history.Count, criticCount, authorName);

        // Quality gate: only check after the critic speaks
        if (lastMessage?.AuthorName == "JokeCritic")
        {
            var messageText = lastMessage.Text ?? string.Empty;

            _logger.LogInformation("Critic response (first 300 chars): {Message}",
                messageText.Length > 300 ? messageText[..300] + "..." : messageText);

            var rating = ExtractRating(messageText);
            _logger.LogInformation("Extracted overall rating: {Rating}/10", rating);

            if (rating >= 8)
            {
                _logger.LogInformation("✅ Quality gate PASSED — {Rating}/10. Terminating.", rating);
                return ValueTask.FromResult(true);
            }

            if (rating == 0)
            {
                _logger.LogWarning("⚠️ Could not parse rating from critic. Continuing.");
            }
            else
            {
                _logger.LogInformation("⚠️ Rating {Rating}/10 < 8 — needs improvement.", rating);
            }
        }

        // Safety net: terminate after MaxRounds critic evaluations
        if (criticCount >= MaxRounds)
        {
            _logger.LogWarning("🛑 Max rounds ({MaxRounds}) reached after {CriticCount} critic responses. Terminating.",
                MaxRounds, criticCount);
            return ValueTask.FromResult(true);
        }

        return ValueTask.FromResult(false);
    }
    
    /// <summary>
    /// Extracts the overall rating from critic's response text.
    /// Handles markdown formatting (e.g., **Rating:** 5/10, ## Rating: 5/10).
    /// Only matches the overall "Rating:" line, never sub-category scores.
    /// </summary>
    public static int ExtractRating(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        // Strip markdown bold/italic markers so "**Rating:**" becomes "Rating:"
        var cleaned = Regex.Replace(text, @"[*_]{1,3}", "");

        // Match "Rating: X/10" at the start of a line, allowing optional leading whitespace and markdown headers.
        // The anchor ensures we don't accidentally match sub-category lines like "Two-Story Gap: 8/10".
        var match = Regex.Match(cleaned, @"^\s*(?:#{1,3}\s*)?Rating:\s*(\d+)\s*/\s*10", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int rating))
        {
            return rating;
        }

        // Try "Overall Rating: X/10" or "Overall: X/10" anywhere
        match = Regex.Match(cleaned, @"Overall\s*(?:Rating)?\s*:\s*(\d+)\s*/\s*10", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out rating))
        {
            return rating;
        }

        return 0;
    }
    
    /// <summary>
    /// Extracts sentence count from critic's response
    /// </summary>
    public static int ExtractSentenceCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        var match = Regex.Match(text, @"Sentence Count:\s*(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
        {
            return count;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Extracts estimated tell time from critic's response
    /// </summary>
    public static int ExtractTellTime(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        var match = Regex.Match(text, @"Estimated Tell Time:\s*(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int seconds))
        {
            return seconds;
        }
        
        return 0;
    }
}
