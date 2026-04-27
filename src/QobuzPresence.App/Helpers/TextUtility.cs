namespace QobuzPresence.Helpers;

internal static class TextUtility
{
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

    public static string NormalizeForComparison(string value)
    {
        return value
            .Trim()
            .Replace("\u2019", "'", StringComparison.Ordinal)
            .Replace("\u2018", "'", StringComparison.Ordinal)
            .Replace("\u201C", "\"", StringComparison.Ordinal)
            .Replace("\u201D", "\"", StringComparison.Ordinal)
            .Replace("â€™", "'", StringComparison.Ordinal)
            .Replace("â€˜", "'", StringComparison.Ordinal)
            .Replace("â€œ", "\"", StringComparison.Ordinal)
            .Replace("â€\u009d", "\"", StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
