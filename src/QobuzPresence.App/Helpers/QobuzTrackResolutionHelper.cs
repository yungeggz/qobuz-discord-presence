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
            return new TrackResolutionResult(
                selectedLookup.Track with { PlaybackTiming = playbackTiming },
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
        string trackTitle = TextUtility.NormalizeForComparison(track.Title);
        string trackArtist = TextUtility.NormalizeForComparison(track.Artist);
        string windowTitle = TextUtility.NormalizeForComparison(windowTrack.Title);
        string windowArtist = TextUtility.NormalizeForComparison(windowTrack.Artist ?? string.Empty);

        bool titleMatches = trackTitle == windowTitle;
        bool artistMatches =
            trackArtist == windowArtist ||
            trackArtist.Contains(windowArtist, StringComparison.OrdinalIgnoreCase) ||
            windowArtist.Contains(trackArtist, StringComparison.OrdinalIgnoreCase);

        return titleMatches && artistMatches;
    }
}
