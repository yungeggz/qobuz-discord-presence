namespace QobuzPresence;

internal static class AppConstants
{
    public const string AppName = "Qobuz Discord Presence";
    public const string AppDataDirectoryName = "QobuzPresence";
    public const string DiagnosticsDirectoryName = "Diagnostics";
    public const string SettingsFileName = "settings.json";
    public const string StartupRegistryValueName = "QobuzPresence";
    public const string MutexName = @"Local\QobuzPresence_User";
    public const string QobuzDirectoryName = "Qobuz";
    public const string QobuzProcessName = "Qobuz";
    public const string QobuzDatabaseFileName = "qobuz.db";
    public const string PlayerFilePattern = "player-*.json";
    public const string UnknownArtist = "Unknown Artist";
    public const string QualityUnavailable = "quality unavailable";
    public const string CachedStreamQualitySource = "Cached stream";
    public const int NotifyIconMaxTextLength = 127;
    public const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/yungeggz/qobuz-discord-presence/releases/latest";
    public const string GitHubLatestReleasePageUrl = "https://github.com/yungeggz/qobuz-discord-presence/releases/latest";
}

internal static class StatusMessages
{
    public const string Paused = "Paused";
    public const string Resumed = "Resumed";
    public const string PresenceCleared = "Presence cleared";
    public const string WaitingForQobuzToOpen = "Waiting for Qobuz to open...";
    public const string WaitingForQobuzPlayback = "Waiting for Qobuz playback...";
    public const string MissingDiscordClientId = "Missing Discord Client ID. Open Settings.";
    public const string DiscordNotConnected = "Discord not connected. Is Discord Desktop running?";
    public const string FailedToUpdatePresence = "Failed to update Discord presence. Will retry.";
}
