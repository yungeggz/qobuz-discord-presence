namespace QobuzPresence.Models;

public sealed record CurrentQueueState(
    long TrackId,
    int CurrentIndex,
    string? QueueItemId,
    PlaybackTiming? PlaybackTiming);