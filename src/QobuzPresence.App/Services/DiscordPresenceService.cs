using DiscordRPC;

using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class DiscordPresenceService : IDisposable
{
    private DiscordRpcClient? _client;
    private string? _clientId;

    private bool _connected;

    public bool IsConnected => _connected;

    public bool Connect(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        if (_client is not null && _connected && string.Equals(_clientId, clientId, StringComparison.Ordinal))
        {
            return true;
        }

        Disconnect();

        try
        {
            _client = new DiscordRpcClient(clientId);
            _client.Initialize();
            _connected = true;
            _clientId = clientId;
            return true;
        }
        catch
        {
            Disconnect();
            return false;
        }
    }

    public bool UpdatePresence(TrackSnapshot track, AppSettings settings)
    {
        if (_client is null || !_connected)
        {
            return false;
        }

        string? qualityText = track.Quality?.DisplayText;
        string state = track.Artist;

        if (settings.DisplayAudioQualityInState && !string.IsNullOrWhiteSpace(qualityText))
        {
            state += $" • {qualityText}";
        }

        string largeText = $"{track.Title} - {track.Artist}";

        if (settings.DisplayAudioQualityInLargeImageHover && !string.IsNullOrWhiteSpace(qualityText))
        {
            largeText += $" • {qualityText}";
        }

        Assets assets = new()
        {
            LargeImageKey = !string.IsNullOrWhiteSpace(track.CoverImageUrl)
                ? track.CoverImageUrl
                : settings.FallbackLargeImageKey,
            LargeImageText = largeText
        };

        RichPresence presence = new()
        {
            Details = track.Title,
            State = state,
            Assets = assets,
            Timestamps = BuildTimestamps(track.Duration, track.PlaybackTiming)
        };

        try
        {
            _client.SetPresence(presence);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ClearPresence()
    {
        try
        {
            _client?.ClearPresence();
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    public void Disconnect()
    {
        try
        {
            _client?.ClearPresence();
            _client?.Dispose();
        }
        catch
        {
            // Ignore cleanup failures.
        }
        finally
        {
            _client = null;
            _connected = false;
            _clientId = null;
        }
    }

    public void Dispose()
    {
        Disconnect();
    }

    private static Timestamps? BuildTimestamps(
      TimeSpan? duration,
      PlaybackTiming? playbackTiming)
    {
        if (!duration.HasValue)
        {
            return null;
        }

        DateTime start = playbackTiming?.StartedAtUtc.UtcDateTime
            ?? DateTime.UtcNow;

        return new Timestamps
        {
            Start = start,
            End = start.Add(duration.Value)
        };
    }
}
