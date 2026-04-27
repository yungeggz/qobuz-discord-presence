using QobuzPresence.Models;

namespace QobuzPresence.Services;

internal static class QobuzWindowTitleParser
{
    public static WindowTrackInfo? Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        title = title.Trim();

        if (title.Equals(AppConstants.QobuzDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (title.Contains(AppConstants.QobuzDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] parts = title.Split(
            " - ",
            2,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return null;
        }

        return new WindowTrackInfo(
            Title: parts[0],
            Artist: parts[1]);
    }
}
