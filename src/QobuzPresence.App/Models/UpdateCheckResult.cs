namespace QobuzPresence.Models;

internal sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string? LatestVersion = null,
    string? ReleaseUrl = null,
    string? ErrorMessage = null);
