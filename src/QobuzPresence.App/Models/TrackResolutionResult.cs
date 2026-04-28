namespace QobuzPresence.Models;

internal enum TrackResolutionSource
{
    SelectedTrackIdLTrack,
    WindowTitleRecoveredLTrack,
    WindowTitleRecoveredSTrack,
    WindowTitleFallbackOnly
}

internal sealed record TrackResolutionResult(
    TrackSnapshot Track,
    TrackResolutionSource Source,
    string? StatusNote = null);
