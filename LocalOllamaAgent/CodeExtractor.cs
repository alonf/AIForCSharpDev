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

        var blocks = ParseFenceBlocks(text);
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var csharpBlock = blocks.LastOrDefault(block => IsCSharpLanguage(block.Language));
        if (!string.IsNullOrWhiteSpace(csharpBlock.Content))
        {
            return csharpBlock.Content.Trim();
        }

        var heuristicBlock = blocks.FirstOrDefault(block => LooksLikeCSharp(block.Content));
        if (!string.IsNullOrWhiteSpace(heuristicBlock.Content))
        {
            return heuristicBlock.Content.Trim();
        }

        string bestNonJson = blocks
            .Where(block => !IsJsonLanguage(block.Language))
            .OrderByDescending(block => block.Content.Length)
            .Select(block => block.Content)
            .FirstOrDefault() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(bestNonJson))
        {
            return bestNonJson.Trim();
        }

        return blocks
            .OrderByDescending(block => block.Content.Length)
            .Select(block => block.Content)
            .FirstOrDefault()?
            .Trim() ?? string.Empty;
    }

    private static List<FenceBlock> ParseFenceBlocks(string text)
    {
        var blocks = new List<FenceBlock>();
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

            var rawBlock = text.Substring(startFence + 3, endFence - startFence - 3);
            var (language, content) = NormalizeBlock(rawBlock);
            if (!string.IsNullOrWhiteSpace(content))
            {
                blocks.Add(new FenceBlock(language, content));
            }

            searchIndex = endFence + 3;
        }

        return blocks;
    }

    private static (string Language, string Content) NormalizeBlock(string block)
    {
        block = block.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = block.Split('\n');
        int startIndex = 0;
        string language = string.Empty;

        if (lines.Length > 0)
        {
            string firstLine = lines[0].Trim();
            if (IsLanguageIdentifier(firstLine))
            {
                language = firstLine;
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

        return (language, string.Join('\n', cleaned).Trim());
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

    private static bool IsCSharpLanguage(string language)
    {
        return language.Equals("csharp", StringComparison.OrdinalIgnoreCase) ||
               language.Equals("cs", StringComparison.OrdinalIgnoreCase) ||
               language.Equals("c#", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJsonLanguage(string language) =>
        language.Equals("json", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeCSharp(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains("class ", StringComparison.Ordinal) ||
               content.Contains("static void Main", StringComparison.Ordinal) ||
               content.Contains("using System", StringComparison.Ordinal);
    }

    private readonly record struct FenceBlock(string Language, string Content);
}
