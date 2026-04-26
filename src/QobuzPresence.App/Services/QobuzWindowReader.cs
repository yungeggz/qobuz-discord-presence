using System.Diagnostics;
using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class QobuzWindowReader
{
    public WindowTrackInfo? GetCurrentWindowTrackInfo()
    {
        Process? process = Process
            .GetProcessesByName("Qobuz")
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle));

        if (process is null)
        {
            return null;
        }

        return ParseWindowTitle(process.MainWindowTitle);
    }

    private static WindowTrackInfo? ParseWindowTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        title = title.Trim();

        if (title.Equals("Qobuz", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (title.Contains("Qobuz", StringComparison.OrdinalIgnoreCase))
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

        // The exact order does not matter much because this is only a playback gate/fallback.
        // The authoritative track info still comes from player-0.json + qobuz.db.
        return new WindowTrackInfo(
            Artist: parts[0],
            Title: parts[1]);
    }
}
