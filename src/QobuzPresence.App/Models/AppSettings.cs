namespace QobuzPresence.Models;

public sealed class AppSettings
{
    public bool DisplayAudioQualityInState { get; set; } = true;

    public bool DisplayAudioQualityInLargeImageHover { get; set; } = true;

    public string FallbackLargeImageKey { get; set; } = "qobuz";

    public bool StartWithWindows { get; set; } = false;

    public bool CheckForUpdatesOnStartup { get; set; } = true;
}
