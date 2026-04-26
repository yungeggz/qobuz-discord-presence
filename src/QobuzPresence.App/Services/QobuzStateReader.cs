using System.Text.Json;
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
            .EnumerateFiles(directory, "player-*.json", SearchOption.TopDirectoryOnly)
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

            if (!TryGetProperty(root, "playqueue", out JsonElement playqueue) ||
                !TryGetProperty(playqueue, "data", out JsonElement playqueueData))
            {
                return null;
            }

            if (!TryGetInt32(playqueueData, "currentIndex", out int currentIndex))
            {
                return null;
            }

            if (!TryGetProperty(playqueueData, "items", out JsonElement items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (currentIndex < 0 || currentIndex >= items.GetArrayLength())
            {
                return null;
            }

            JsonElement currentItem = items[currentIndex];

            if (!TryGetInt64(currentItem, "trackId", out long trackId))
            {
                return null;
            }

            string? queueItemId = TryGetString(currentItem, "queueItemId");
            PlaybackTiming? playbackTiming = TryReadPlaybackTiming(root);

            return new CurrentQueueState(trackId, currentIndex, queueItemId, playbackTiming);

        }
        catch
        {
            return null;
        }
    }

    private static PlaybackTiming? TryReadPlaybackTiming(JsonElement root)
    {
        if (!TryGetProperty(root, "player", out JsonElement player) ||
            !TryGetProperty(player, "data", out JsonElement playerData) ||
            !TryGetProperty(playerData, "position", out JsonElement position))
        {
            return null;
        }

        if (!TryGetInt64(position, "value", out long positionMilliseconds))
        {
            return null;
        }

        if (!TryGetInt64(position, "timestamp", out long timestampMilliseconds))
        {
            return null;
        }

        try
        {
            TimeSpan currentPosition = TimeSpan.FromMilliseconds(positionMilliseconds);
            DateTimeOffset reportedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(timestampMilliseconds);

            return new PlaybackTiming(currentPosition, reportedAtUtc);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = default;

        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = default;

        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }
}
