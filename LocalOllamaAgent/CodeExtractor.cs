namespace LocalOllamaAgent;

/// <summary>
/// Utility for extracting C# code from markdown-fenced blocks.
/// </summary>
public static class CodeExtractor
{
    /// <summary>
    /// Extracts code from markdown triple-backtick fenced blocks.
    /// Handles optional language identifier (e.g., ```csharp) and removes accidental 'CODE_READY' lines.
    /// </summary>
    public static string Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string bestBlock = string.Empty;
        int searchIndex = 0;

        while (true)
        {
            int startFence = text.IndexOf("```", searchIndex, StringComparison.Ordinal);
            if (startFence < 0)
            {
                break;
            }

            int endFence = text.IndexOf("```", startFence + 3, StringComparison.Ordinal);
            if (endFence < 0)
            {
                break;
            }

            var block = text.Substring(startFence + 3, endFence - startFence - 3);
            var cleaned = NormalizeBlock(block);
            if (cleaned.Length > bestBlock.Length)
            {
                bestBlock = cleaned;
            }

            searchIndex = endFence + 3;
        }

        return bestBlock.Trim();
    }

    private static string NormalizeBlock(string block)
    {
        block = block.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = block.Split('\n');
        int startIndex = 0;

        if (lines.Length > 0)
        {
            string firstLine = lines[0].Trim();
            if (IsLanguageIdentifier(firstLine))
            {
                startIndex = 1;
            }
        }

        var cleaned = new List<string>(lines.Length - startIndex);
        for (int i = startIndex; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Equals("CODE_READY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            cleaned.Add(lines[i]);
        }

        return string.Join('\n', cleaned).Trim();
    }

    private static bool IsLanguageIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains(' ') || value.Contains('\t') || value.Contains(';'))
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c == '#' || c == '+' || c == '.'))
            {
                return false;
            }
        }

        return true;
    }
}