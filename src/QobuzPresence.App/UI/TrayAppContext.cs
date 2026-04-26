using System.Reflection;

using QobuzPresence.Models;
using QobuzPresence.Services;

namespace QobuzPresence.UI;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly PresenceCoordinator _coordinator;
    private readonly SynchronizationContext _uiContext;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _pauseMenuItem;
    private readonly ToolStripMenuItem _startWithWindowsMenuItem;

    public TrayAppContext(
        SettingsService settingsService,
        StartupService startupService,
        QobuzStateReader stateReader,
        QobuzTrackReader trackReader,
        QobuzWindowReader windowReader,
        DiscordPresenceService discordService)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _coordinator = new PresenceCoordinator(
            settingsService,
            stateReader,
            trackReader,
            windowReader,
            discordService);
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _statusMenuItem = new ToolStripMenuItem("Starting...")
        {
            Enabled = false
        };

        _pauseMenuItem = new ToolStripMenuItem("Pause Presence", null, TogglePause_Click);
        _startWithWindowsMenuItem = new ToolStripMenuItem("Start with Windows", null, ToggleStartWithWindows_Click)
        {
            Checked = _coordinator.Settings.StartWithWindows || _startupService.IsEnabled()
        };

        ContextMenuStrip menu = new();
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open Settings", null, OpenSettings_Click));
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(new ToolStripMenuItem("Reconnect Discord", null, Reconnect_Click));
        menu.Items.Add(new ToolStripMenuItem("Clear Presence", null, ClearPresence_Click));
        menu.Items.Add(_startWithWindowsMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, Exit_Click));

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Qobuz Discord Presence",
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += OpenSettings_Click;

        _coordinator.StatusChanged += Coordinator_StatusChanged;
        _coordinator.TrackChanged += Coordinator_TrackChanged;
        _coordinator.Start();

        if (string.IsNullOrWhiteSpace(DiscordApplication.ClientId))
        {
            BeginInvokeOpenSettings();
        }
    }

    private void Coordinator_StatusChanged(object? sender, string status)
    {
        _uiContext.Post(_ => SetStatus(status), null);
    }

    private void Coordinator_TrackChanged(object? sender, TrackSnapshot? track)
    {
        _uiContext.Post(_ =>
        {
            _notifyIcon.Text = track is null
                ? "Qobuz Discord Presence"
                : BuildTrayTooltip(track);
        }, null);
    }

    private static string BuildTrayTooltip(TrackSnapshot track)
    {
        string text = $"Qobuz Discord Presence: {track.Artist} - {track.Title}";

        // NotifyIcon.Text has a fairly small tooltip length limit.
        // Keep it safe so long titles do not throw or get awkwardly clipped.
        const int maxLength = 127;

        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 1)] + "…";
    }

    private void SetStatus(string status)
    {
        _statusMenuItem.Text = status.Length > 80 ? status[..77] + "..." : status;
    }

    private void BeginInvokeOpenSettings()
    {
        System.Windows.Forms.Timer timer = new()
        {
            Interval = 500
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            ShowSettings();
        };

        timer.Start();
    }

    private void OpenSettings_Click(object? sender, EventArgs e)
    {
        ShowSettings();
    }

    private void ShowSettings()
    {
        AppSettings settings = _settingsService.Load();

        using SettingsForm form = new(_settingsService, _startupService, settings);

        if (form.ShowDialog() == DialogResult.OK)
        {
            _coordinator.ReloadSettings();
            _startWithWindowsMenuItem.Checked = _startupService.IsEnabled();
        }
    }

    private void TogglePause_Click(object? sender, EventArgs e)
    {
        bool nextPausedState = !_coordinator.IsPaused;
        _coordinator.SetPaused(nextPausedState);
        _pauseMenuItem.Text = nextPausedState ? "Resume Presence" : "Pause Presence";
    }

    private void Reconnect_Click(object? sender, EventArgs e)
    {
        _coordinator.ReconnectDiscord();
    }

    private void ClearPresence_Click(object? sender, EventArgs e)
    {
        _coordinator.ClearPresence();
    }

    private void ToggleStartWithWindows_Click(object? sender, EventArgs e)
    {
        bool nextValue = !_startWithWindowsMenuItem.Checked;
        _startupService.SetEnabled(nextValue);
        _startWithWindowsMenuItem.Checked = nextValue;

        AppSettings settings = _settingsService.Load();
        settings.StartWithWindows = nextValue;
        _settingsService.Save(settings);
        _coordinator.ReloadSettings();
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _coordinator.Dispose();
        }

        base.Dispose(disposing);
    }

    public static Icon LoadTrayIcon()
    {
        Assembly assembly = typeof(TrayAppContext).Assembly;

        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("qobuz-presence.ico", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return SystemIcons.Application;
        }

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);

        return stream is not null
            ? new Icon(stream)
            : SystemIcons.Application;
    }
}
