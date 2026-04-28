namespace QobuzPresence.Helpers;

internal static class VersionUtility
{
    public static string NormalizeReleaseVersion(string version)
    {
        string normalized = version.Trim();

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        int metadataSeparatorIndex = normalized.IndexOfAny(['-', '+']);

        if (metadataSeparatorIndex >= 0)
        {
            normalized = normalized[..metadataSeparatorIndex];
        }

        return normalized;
    }

    public static bool TryParseComparableVersion(string version, out Version? comparableVersion)
    {
        comparableVersion = null;

        string normalized = NormalizeReleaseVersion(version);
        string[] parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 3 || parts.Length > 4)
        {
            return false;
        }

        if (!parts.All(part => int.TryParse(part, out _)))
        {
            return false;
        }

        if (parts.Length == 3)
        {
            normalized += ".0";
        }

        return Version.TryParse(normalized, out comparableVersion);
    }
}
