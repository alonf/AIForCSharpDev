using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace JokeAgentsDemo;

/// <summary>
/// Custom Group Chat Manager with quality gate for joke rating
/// </summary>
public class JokeQualityManager : RoundRobinGroupChatManager
{
    private readonly ILogger _logger;
    
    public JokeQualityManager(IReadOnlyList<AIAgent> agents, ILogger logger) 
        : base(agents)
    {
        MaximumIterationCount = 5;  // Safety limit
        _logger = logger;
    }

    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, 
        CancellationToken cancellationToken = default)
    {
        var lastMessage = history.LastOrDefault();
        
        _logger.LogInformation("Checking termination. Last message from: {Author}", lastMessage?.AuthorName ?? "null");
        
        if (lastMessage?.AuthorName == "JokeCritic")
        {
            var messageText = lastMessage.Text;
            
            _logger.LogInformation("Critic message: {Message}", messageText.Length > 200 ? messageText.Substring(0, 200) + "..." : messageText);
            
            // Try to extract rating first
            var rating = ExtractRating(messageText);
            _logger.LogInformation("Extracted rating: {Rating}", rating);
            
            // Only terminate if rating is 8 or higher
            if (rating >= 8)
            {
                _logger.LogInformation("✅ Joke rated {Rating}/10 - Quality threshold met!", rating);
                return ValueTask.FromResult(true);
            }
            
            // REMOVED: The fallback "APPROVED" check that allowed 6/10 jokes to pass
            // The critic sometimes says "APPROVED" even when rating < 8, which is a mistake
            // We should ONLY trust the numeric rating
            
            _logger.LogInformation("⚠️ Joke rated {Rating}/10 - Needs improvement", rating);
        }
        
        return ValueTask.FromResult(false);
    }
    
    /// <summary>
    /// Extracts rating from critic's response text
    /// </summary>
    public static int ExtractRating(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        // Try to find "Rating: X/10" pattern (most specific)
        var match = Regex.Match(text, @"Rating:\s*(\d+)/10", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int rating))
        {
            return rating;
        }
        
        // Try "Overall Rating: X/10" or "Overall: X/10"
        match = Regex.Match(text, @"Overall\s*(?:Rating)?:\s*(\d+)/10", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out rating))
        {
            return rating;
        }
        
        // Fallback: look for any standalone number followed by /10
        match = Regex.Match(text, @"(?:^|\s)(\d+)/10", RegexOptions.Multiline);
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
