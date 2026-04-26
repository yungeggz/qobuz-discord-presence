namespace QobuzPresence.Models;

public sealed record TrackSnapshot(
    long TrackId,
    string Title,
    string Artist,
    AudioQuality? Quality,
    string? CoverImageUrl,
    TimeSpan? Duration,
    PlaybackTiming? PlaybackTiming = null);