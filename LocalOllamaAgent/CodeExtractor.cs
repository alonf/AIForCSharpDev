namespace LocalOllamaAgent;

/// <summary>
/// Utility for extracting C# code from markdown-fenced blocks.
/// </summary>
public static class CodeExtractor
{
    /// <summary>
    /// Extracts code from markdown triple-backtick fenced blocks.
    /// Handles optional language identifier (e.g., ```csharp).
    /// </summary>
    public static string Extract(string text)
    {
        int i = text.IndexOf("```", StringComparison.Ordinal);
        if (i >= 0)
        {
            int j = text.IndexOf("```", i + 3, StringComparison.Ordinal);
            if (j > i)
            {
                var block = text.Substring(i + 3, j - i - 3);
                
                // Find the first newline to check if there's a language identifier
                int firstNewline = block.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    string firstLine = block.Substring(0, firstNewline).Trim();
                    
                    // If the first line is a language identifier (like "csharp"), skip it
                    if (!string.IsNullOrWhiteSpace(firstLine) && 
                        !firstLine.Contains(' ') && 
                        !firstLine.Contains(';') &&
                        firstLine.All(c => char.IsLetter(c) || c == '#' || c == '+'))
                    {
                        return block.Substring(firstNewline + 1).Trim();
                    }
                }
                
                return block.Trim();
            }
        }
        return string.Empty;
    }
}