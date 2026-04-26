namespace QobuzPresence.Models;

public sealed record PresenceSignature(
    long TrackId,
    string Title,
    string Artist,
    string? QualityText,
    string? CoverImageUrl,
    long? TimerStartUnixSeconds,
    long? TimerEndUnixSeconds,
    bool DisplayAudioQualityInState,
    bool DisplayAudioQualityInLargeImageHover,
    string FallbackLargeImageKey);

public sealed record PresenceSettingsSignature(
    bool DisplayAudioQualityInState,
    bool DisplayAudioQualityInLargeImageHover,
    string FallbackLargeImageKey);