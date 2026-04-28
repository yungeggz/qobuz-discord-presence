using System.Diagnostics;
using QobuzPresence.Helpers;
using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class PresenceCoordinator : IDisposable
{
    private static readonly TimeSpan s_activePollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_periodicPresenceRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] s_qualityRetrySchedule =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20)
    ];
    private static readonly TimeSpan[] s_presenceRefreshRetrySchedule =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
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
    private int _presenceRefreshRetryIndex;
    private DateTimeOffset? _lastPresenceSentAtUtc;
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
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.Zero, s_activePollInterval);
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
            OnStatusChanged(StatusMessages.Paused);
        }
        else
        {
            ResetPresenceTracking();
            OnStatusChanged(StatusMessages.Resumed);
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
        OnStatusChanged(StatusMessages.PresenceCleared);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _tickLock.Dispose();
        _discordService.Dispose();
    }

    private static bool IsQobuzRunning()
    {
        Process[] processes = Process.GetProcessesByName(AppConstants.QobuzProcessName);

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
        _presenceRefreshRetryIndex = 0;
        _lastPresenceSentAtUtc = null;
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
        if (_qualityResolvedForCurrentTrack || _qualityRetryIndex >= s_qualityRetrySchedule.Length)
        {
            return false;
        }

        return now - _trackDetectedAtUtc >= s_qualityRetrySchedule[_qualityRetryIndex];
    }

    private bool IsPresenceRefreshRetryDue(DateTimeOffset now)
    {
        if (_presenceRefreshRetryIndex >= s_presenceRefreshRetrySchedule.Length)
        {
            return false;
        }

        return now - _trackDetectedAtUtc >= s_presenceRefreshRetrySchedule[_presenceRefreshRetryIndex];
    }

    private void TickCore()
    {
        if (_paused)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DiscordApplication.ClientId))
        {
            OnStatusChanged(StatusMessages.MissingDiscordClientId);
            return;
        }

        if (!_discordService.Connect(DiscordApplication.ClientId))
        {
            ResetPresenceTracking();
            OnTrackChanged(null);
            OnStatusChanged(StatusMessages.DiscordNotConnected);
            return;
        }

        if (!IsQobuzRunning())
        {
            ClearPresenceWhileWaiting(StatusMessages.WaitingForQobuzToOpen);
            return;
        }

        WindowTrackInfo? activeWindowTrack = _windowReader.GetCurrentWindowTrackInfo();

        if (activeWindowTrack is null)
        {
            ClearPresenceWhileWaiting(StatusMessages.WaitingForQobuzPlayback);
            return;
        }

        CurrentQueueState? queueState = _stateReader.GetCurrentQueueState();

        if (queueState is null)
        {
            ClearPresenceWhileWaiting(StatusMessages.WaitingForQobuzPlayback);
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
            _presenceRefreshRetryIndex = 0;
        }

        bool qualityRetryDue = !isNewTrack && IsQualityRetryDue(now);
        bool presenceRefreshRetryDue = !isNewTrack && IsPresenceRefreshRetryDue(now);
        bool periodicPresenceRefreshDue =
            _lastPresenceSentAtUtc.HasValue &&
            now - _lastPresenceSentAtUtc.Value >= s_periodicPresenceRefreshInterval;
        bool forcePresenceRefresh = presenceRefreshRetryDue || periodicPresenceRefreshDue;

        if (!isNewTrack && !timerBaselineChanged && !qualityRetryDue && !presenceSettingsChanged && !forcePresenceRefresh)
        {
            return;
        }

        if (qualityRetryDue)
        {
            _qualityRetryIndex++;
        }

        if (presenceRefreshRetryDue)
        {
            _presenceRefreshRetryIndex++;
        }

        TrackResolutionResult resolution = QobuzTrackResolutionHelper.Resolve(
            _trackReader,
            queueState.TrackId,
            queueState.PlaybackTiming,
            activeWindowTrack);

        if (!string.IsNullOrWhiteSpace(resolution.StatusNote))
        {
            OnStatusChanged(resolution.StatusNote);
        }

        TrackSnapshot track = resolution.Track;

        if (track.Quality is not null)
        {
            _qualityResolvedForCurrentTrack = true;
        }

        PresenceSignature presenceSignature = BuildPresenceSignature(track, _settings);

        if (!forcePresenceRefresh && presenceSignature == _lastPresenceSignature)
        {
            return;
        }

        if (_discordService.UpdatePresence(track, _settings))
        {
            _lastTrackId = queueState.TrackId;
            _lastPlaybackStartUtc = queueState.PlaybackTiming?.StartedAtUtc;
            _lastPresenceSignature = presenceSignature;
            _lastPresenceSettingsSignature = currentSettingsSignature;
            _lastPresenceSentAtUtc = now;
            _presenceClearedWhileWaiting = false;

            OnTrackChanged(track);

            string quality = track.Quality?.DisplayText ?? AppConstants.QualityUnavailable;
            OnStatusChanged($"Showing {track.Title} - {track.Artist} ({quality})");
        }
        else
        {
            ResetPresenceTracking();
            _discordService.Disconnect();
            OnTrackChanged(null);
            OnStatusChanged(StatusMessages.FailedToUpdatePresence);
        }
    }

    private bool HasTimerBaselineChanged(PlaybackTiming? playbackTiming)
    {
        if (playbackTiming is null)
        {
            return false;
        }

        if (!_lastPlaybackStartUtc.HasValue)
        {
            return true;
        }

        TimeSpan drift = playbackTiming.StartedAtUtc - _lastPlaybackStartUtc.Value;
        return Math.Abs(drift.TotalSeconds) >= 3;
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
