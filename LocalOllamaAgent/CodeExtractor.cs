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
                var block = text[(i + 3)..j];
                var parts = block.Split('\n');
                if (parts.Length > 1 && parts[0].StartsWith("csharp", StringComparison.OrdinalIgnoreCase))
                    return string.Join('\n', parts.Skip(1));
                return block.Trim();
            }
        }
        return string.Empty;
    }
}