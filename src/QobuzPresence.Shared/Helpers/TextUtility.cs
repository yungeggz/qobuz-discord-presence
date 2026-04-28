namespace QobuzPresence.Helpers;

public static class TextUtility
{
    private static readonly char[] s_trailingDecorationTrimChars = [' ', '-', ':'];

    public static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public static bool ContainsInsensitive(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return left.Contains(right, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEquivalentOrContained(string left, string right)
    {
        string normalizedLeft = NormalizeForComparison(left);
        string normalizedRight = NormalizeForComparison(right);

        return normalizedLeft == normalizedRight ||
            normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) ||
            normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal);
    }

    public static string StripBracketedDecorations(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string stripped = RemoveBracketedSegments(value, '(', ')');
        stripped = RemoveBracketedSegments(stripped, '[', ']');

        return stripped.Trim().TrimEnd(s_trailingDecorationTrimChars);
    }

    public static string NormalizeForComparison(string value)
    {
        return value
            .Trim()
            .Replace("\u2019", "'", StringComparison.Ordinal)
            .Replace("\u2018", "'", StringComparison.Ordinal)
            .Replace("\u201C", "\"", StringComparison.Ordinal)
            .Replace("\u201D", "\"", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬â„¢", "'", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬Ëœ", "'", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬Å“", "\"", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬\u009d", "\"", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string RemoveBracketedSegments(string value, char openingBracket, char closingBracket)
    {
        int startIndex = value.IndexOf(openingBracket, StringComparison.Ordinal);

        while (startIndex >= 0)
        {
            int endIndex = value.IndexOf(closingBracket, startIndex + 1);

            if (endIndex < 0)
            {
                break;
            }

            value = value.Remove(startIndex, endIndex - startIndex + 1);
            startIndex = value.IndexOf(openingBracket, StringComparison.Ordinal);
        }

        return value;
    }
}
