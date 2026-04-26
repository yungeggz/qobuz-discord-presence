using System.Diagnostics;

using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class PresenceCoordinator : IDisposable
{
    private static readonly TimeSpan s_activePollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan[] s_qualityRetrySchedule =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20)
    ];

    private readonly SettingsService _settingsService;
    private readonly QobuzStateReader _stateReader;
    private readonly QobuzTrackReader _trackReader;
    private readonly QobuzWindowReader _windowReader;
    private readonly DiscordPresenceService _discordService;
    private readonly SemaphoreSlim _tickLock = new(1, 1);

    private System.Threading.Timer? _timer;
    private AppSettings _settings;
    private long? _lastTrackId;
    private DateTimeOffset? _lastPlaybackStartUtc;
    private DateTimeOffset _trackDetectedAtUtc = DateTimeOffset.MinValue;
    private bool _paused;

    private int _qualityRetryIndex;
    private bool _qualityResolvedForCurrentTrack;

    private PresenceSignature? _lastPresenceSignature;
    private PresenceSettingsSignature? _lastPresenceSettingsSignature;
    private bool _presenceClearedWhileWaiting;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<TrackSnapshot?>? TrackChanged;

    public PresenceCoordinator(
        SettingsService settingsService,
        QobuzStateReader stateReader,
        QobuzTrackReader trackReader,
        QobuzWindowReader windowReader,
        DiscordPresenceService discordService)
    {
        _settingsService = settingsService;
        _stateReader = stateReader;
        _trackReader = trackReader;
        _windowReader = windowReader;
        _discordService = discordService;
        _settings = _settingsService.Load();
    }

    public AppSettings Settings => _settings;

    public bool IsPaused => _paused;

    public void Start()
    {
        TimeSpan interval = s_activePollInterval;
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.Zero, interval);
    }

    public void ReloadSettings()
    {
        _settings = _settingsService.Load();
        ReconnectDiscord();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;

        if (_paused)
        {
            _discordService.ClearPresence();
            ResetPresenceTracking();
            OnTrackChanged(null);
            OnStatusChanged("Paused");
        }
        else
        {
            ResetPresenceTracking();
            OnStatusChanged("Resumed");
            _ = TickAsync();
        }
    }

    public void ReconnectDiscord()
    {
        _discordService.Disconnect();
        ResetPresenceTracking();
        _ = TickAsync();
    }

    public void ClearPresence()
    {
        _discordService.ClearPresence();
        ResetPresenceTracking();
        OnTrackChanged(null);
        OnStatusChanged("Presence cleared");
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _tickLock.Dispose();
        _discordService.Dispose();
    }

    private static bool IsQobuzRunning()
    {
        Process[] processes = Process.GetProcessesByName("Qobuz");

        try
        {
            return processes.Any(process => !process.HasExited);
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void ClearPresenceWhileWaiting(string status)
    {
        if (!_presenceClearedWhileWaiting)
        {
            _discordService.ClearPresence();
            ResetPresenceTracking();
            OnTrackChanged(null);
            _presenceClearedWhileWaiting = true;
        }

        OnStatusChanged(status);
    }

    private void ResetPresenceTracking()
    {
        _lastTrackId = null;
        _lastPlaybackStartUtc = null;
        _lastPresenceSignature = null;
        _lastPresenceSettingsSignature = null;
        _trackDetectedAtUtc = DateTimeOffset.MinValue;
        _qualityResolvedForCurrentTrack = false;
        _qualityRetryIndex = 0;
    }

    private async Task TickAsync()
    {
        if (!await _tickLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            TickCore();
        }
        finally
        {
            _tickLock.Release();
        }
    }

    private bool IsQualityRetryDue(DateTimeOffset now)
    {
        if (_qualityResolvedForCurrentTrack)
        {
            return false;
        }

        if (_qualityRetryIndex >= s_qualityRetrySchedule.Length)
        {
            return false;
        }

        TimeSpan retryDelay = s_qualityRetrySchedule[_qualityRetryIndex];

        return now - _trackDetectedAtUtc >= retryDelay;
    }

    private void TickCore()
    {
        if (_paused)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DiscordApplication.ClientId))
        {
            OnStatusChanged("Missing Discord Client ID. Open Settings.");
            return;
        }

        if (!_discordService.Connect(DiscordApplication.ClientId))
        {
            ResetPresenceTracking();
            OnTrackChanged(null);
            OnStatusChanged("Discord not connected. Is Discord Desktop running?");
            return;
        }

        if (!IsQobuzRunning())
        {
            ClearPresenceWhileWaiting("Waiting for Qobuz to open...");
            return;
        }

        WindowTrackInfo? activeWindowTrack = _windowReader.GetCurrentWindowTrackInfo();

        if (activeWindowTrack is null)
        {
            ClearPresenceWhileWaiting("Waiting for Qobuz playback...");
            return;
        }

        CurrentQueueState? queueState = _stateReader.GetCurrentQueueState();

        if (queueState is null)
        {
            ClearPresenceWhileWaiting("Waiting for Qobuz playback...");
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        bool isNewTrack = _lastTrackId != queueState.TrackId;
        bool timerBaselineChanged = HasTimerBaselineChanged(queueState.PlaybackTiming);

        PresenceSettingsSignature currentSettingsSignature = BuildPresenceSettingsSignature(_settings);
        bool presenceSettingsChanged = currentSettingsSignature != _lastPresenceSettingsSignature;

        if (isNewTrack)
        {
            _trackDetectedAtUtc = now;
            _qualityRetryIndex = 0;
            _qualityResolvedForCurrentTrack = false;
        }

        bool qualityRetryDue = !isNewTrack && IsQualityRetryDue(now);

        if (!isNewTrack && !timerBaselineChanged && !qualityRetryDue && !presenceSettingsChanged)
        {
            return;
        }

        if (qualityRetryDue)
        {
            _qualityRetryIndex++;
        }

        TrackSnapshot? track = _trackReader.GetTrack(queueState.TrackId)
            ?? BuildFallbackTrack(queueState.TrackId, queueState.PlaybackTiming, activeWindowTrack);

        if (track is null)
        {
            OnStatusChanged($"Track {queueState.TrackId} not found in Qobuz cache yet.");
            return;
        }

        track = track with
        {
            PlaybackTiming = queueState.PlaybackTiming
        };

        if (track.Quality is not null)
        {
            _qualityResolvedForCurrentTrack = true;
        }

        PresenceSignature presenceSignature = BuildPresenceSignature(track, _settings);

        if (presenceSignature == _lastPresenceSignature)
        {
            return;
        }

        if (_discordService.UpdatePresence(track, _settings))
        {
            _lastTrackId = queueState.TrackId;
            _lastPlaybackStartUtc = queueState.PlaybackTiming?.StartedAtUtc;
            _lastPresenceSignature = presenceSignature;
            _lastPresenceSettingsSignature = currentSettingsSignature;
            _presenceClearedWhileWaiting = false;

            OnTrackChanged(track);

            string quality = track.Quality?.DisplayText ?? "quality unavailable";
            OnStatusChanged($"Showing {track.Title} - {track.Artist} ({quality})");
        }
        else
        {
            ResetPresenceTracking();
            _discordService.Disconnect();
            OnTrackChanged(null);
            OnStatusChanged("Failed to update Discord presence. Will retry.");
        }
    }

    private bool HasTimerBaselineChanged(PlaybackTiming? playbackTiming)
    {
        if (playbackTiming is null)
        {
            return false;
        }

        DateTimeOffset currentStartUtc = playbackTiming.StartedAtUtc;

        if (!_lastPlaybackStartUtc.HasValue)
        {
            return true;
        }

        TimeSpan drift = currentStartUtc - _lastPlaybackStartUtc.Value;

        return Math.Abs(drift.TotalSeconds) >= 3;
    }

    private static TrackSnapshot? BuildFallbackTrack(long trackId, PlaybackTiming? playbackTiming, WindowTrackInfo windowTrack)
    {
        if (windowTrack is null)
        {
            return null;
        }

        return new TrackSnapshot(
            trackId,
            windowTrack.Title,
            windowTrack.Artist ?? "Unknown Artist",
            null,
            null,
            null,
            playbackTiming);
    }

    private void OnStatusChanged(string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void OnTrackChanged(TrackSnapshot? track)
    {
        TrackChanged?.Invoke(this, track);
    }

    private static PresenceSettingsSignature BuildPresenceSettingsSignature(AppSettings settings)
    {
        return new PresenceSettingsSignature(
            settings.DisplayAudioQualityInState,
            settings.DisplayAudioQualityInLargeImageHover,
            settings.FallbackLargeImageKey);
    }

    private static PresenceSignature BuildPresenceSignature(TrackSnapshot track, AppSettings settings)
    {
        long? timerStartUnixSeconds = null;
        long? timerEndUnixSeconds = null;

        if (track.PlaybackTiming is not null)
        {
            DateTimeOffset startedAtUtc = track.PlaybackTiming.StartedAtUtc;

            timerStartUnixSeconds = startedAtUtc.ToUnixTimeSeconds();

            if (track.Duration.HasValue)
            {
                timerEndUnixSeconds = startedAtUtc
                    .Add(track.Duration.Value)
                    .ToUnixTimeSeconds();
            }
        }

        return new PresenceSignature(
            track.TrackId,
            track.Title,
            track.Artist,
            track.Quality?.DisplayText,
            track.CoverImageUrl,
            timerStartUnixSeconds,
            timerEndUnixSeconds,
            settings.DisplayAudioQualityInState,
            settings.DisplayAudioQualityInLargeImageHover,
            settings.FallbackLargeImageKey);
    }
}
