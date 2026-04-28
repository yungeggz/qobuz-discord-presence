using QobuzPresence.Models;
using QobuzPresence.Services;

namespace QobuzPresence.Helpers;

internal static class QobuzTrackResolutionHelper
{
    public static TrackResolutionResult Resolve(
        QobuzTrackReader trackReader,
        long selectedTrackId,
        PlaybackTiming? playbackTiming,
        WindowTrackInfo activeWindowTrack)
    {
        QobuzTrackReader.TrackLookupResult? selectedLookup = trackReader.GetTrackLookup(selectedTrackId);

        if (selectedLookup is not null && TrackMatchesWindow(selectedLookup.Track, activeWindowTrack))
        {
            TrackSnapshot resolvedTrack = selectedLookup.Track;

            if (ShouldPreferWindowTitle(selectedLookup.Track.Title, activeWindowTrack.Title))
            {
                resolvedTrack = resolvedTrack with { Title = activeWindowTrack.Title };
            }

            return new TrackResolutionResult(
                resolvedTrack with { PlaybackTiming = playbackTiming },
                TrackResolutionSource.SelectedTrackIdLTrack);
        }

        string? statusNote = null;

        if (selectedLookup is not null)
        {
            statusNote =
                $"Qobuz queue state mismatch. Window says {activeWindowTrack.Title} - {activeWindowTrack.Artist}, " +
                $"but player state resolved {selectedLookup.Track.Title} - {selectedLookup.Track.Artist}.";
        }

        QobuzTrackReader.TrackLookupResult? recoveredLookup = trackReader.FindByTitleAndArtistLookup(
            activeWindowTrack.Title,
            activeWindowTrack.Artist);

        if (recoveredLookup is not null)
        {
            TrackResolutionSource source = recoveredLookup.Source switch
            {
                QobuzTrackReader.TrackLookupSource.LTrackByTitleAndArtist => TrackResolutionSource.WindowTitleRecoveredLTrack,
                QobuzTrackReader.TrackLookupSource.STrackByTitleAndArtist => TrackResolutionSource.WindowTitleRecoveredSTrack,
                _ => TrackResolutionSource.WindowTitleRecoveredLTrack
            };

            return new TrackResolutionResult(
                recoveredLookup.Track with { PlaybackTiming = playbackTiming },
                source,
                statusNote);
        }

        TrackSnapshot fallbackTrack = new(
            selectedTrackId,
            activeWindowTrack.Title,
            activeWindowTrack.Artist ?? AppConstants.UnknownArtist,
            null,
            null,
            null,
            playbackTiming);

        return new TrackResolutionResult(
            fallbackTrack,
            TrackResolutionSource.WindowTitleFallbackOnly,
            statusNote);
    }

    private static bool TrackMatchesWindow(TrackSnapshot track, WindowTrackInfo windowTrack)
    {
        return TrackMatchingUtility.TrackMatchesWindow(
            track.Title,
            track.Artist,
            windowTrack.Title,
            windowTrack.Artist);
    }

    private static bool ShouldPreferWindowTitle(string trackTitle, string windowTitle)
    {
        return TrackMatchingUtility.ShouldPreferWindowTitle(trackTitle, windowTitle);
    }
}
