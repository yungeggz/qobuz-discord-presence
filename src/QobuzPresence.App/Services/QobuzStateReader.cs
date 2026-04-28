using System.Text.Json;
using QobuzPresence.Helpers;
using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class QobuzStateReader
{
    public CurrentQueueState? GetCurrentQueueState()
    {
        string? directory = QobuzPaths.GetQobuzRoamingDirectory();

        if (directory is null)
        {
            return null;
        }

        IEnumerable<string> playerFiles = Directory
            .EnumerateFiles(directory, AppConstants.PlayerFilePattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc);

        foreach (string path in playerFiles)
        {
            CurrentQueueState? state = TryReadPlayerFile(path);

            if (state is not null)
            {
                return state;
            }
        }

        return null;
    }

    private static CurrentQueueState? TryReadPlayerFile(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);

            JsonElement root = document.RootElement;

            if (!JsonElementHelper.TryGetNestedProperty(root, out JsonElement playqueueData, "playqueue", "data"))
            {
                return null;
            }

            int? currentIndex = JsonElementHelper.GetInt32(playqueueData, "currentIndex");

            if (!currentIndex.HasValue)
            {
                return null;
            }

            if (!JsonElementHelper.TryGetProperty(playqueueData, "items", out JsonElement items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (currentIndex.Value < 0 || currentIndex.Value >= items.GetArrayLength())
            {
                return null;
            }

            JsonElement currentItem = items[currentIndex.Value];

            long? trackId = JsonElementHelper.GetInt64(currentItem, "trackId");

            if (!trackId.HasValue)
            {
                return null;
            }

            string? queueItemId = JsonElementHelper.GetString(currentItem, "queueItemId");
            PlaybackTiming? playbackTiming = TryReadPlaybackTiming(root);

            return new CurrentQueueState(trackId.Value, currentIndex.Value, queueItemId, playbackTiming);

        }
        catch
        {
            return null;
        }
    }

    private static PlaybackTiming? TryReadPlaybackTiming(JsonElement root)
    {
        if (!JsonElementHelper.TryGetNestedProperty(root, out JsonElement position, "player", "data", "position"))
        {
            return null;
        }

        long? positionMilliseconds = JsonElementHelper.GetInt64(position, "value");

        if (!positionMilliseconds.HasValue)
        {
            return null;
        }

        long? timestampMilliseconds = JsonElementHelper.GetInt64(position, "timestamp");

        if (!timestampMilliseconds.HasValue)
        {
            return null;
        }

        try
        {
            TimeSpan currentPosition = TimeSpan.FromMilliseconds(positionMilliseconds.Value);
            DateTimeOffset reportedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(timestampMilliseconds.Value);

            return new PlaybackTiming(currentPosition, reportedAtUtc);
        }
        catch
        {
            return null;
        }
    }

}
