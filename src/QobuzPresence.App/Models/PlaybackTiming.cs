namespace QobuzPresence.Models;

public sealed record PlaybackTiming(
    TimeSpan Position,
    DateTimeOffset ReportedAtUtc)
{
    public DateTimeOffset StartedAtUtc => ReportedAtUtc - Position;

    public DateTimeOffset? GetEndedAtUtc(TimeSpan? duration)
    {
        return duration.HasValue
            ? StartedAtUtc + duration.Value
            : null;
    }
}