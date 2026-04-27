using System.Reflection;

using QobuzPresence.Models;
using QobuzPresence.Services;

namespace QobuzPresence.UI;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly PresenceCoordinator _coordinator;
    private readonly UpdateCheckService _updateCheckService;
    private readonly SynchronizationContext _uiContext;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _pauseMenuItem;
    private readonly ToolStripMenuItem _startWithWindowsMenuItem;
    private readonly ToolStripMenuItem _checkForUpdatesMenuItem;
    private readonly ToolStripMenuItem _updateAvailableMenuItem;

    private string _latestReleaseUrl = AppConstants.GitHubLatestReleasePageUrl;

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
        _updateCheckService = new UpdateCheckService();
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
        _checkForUpdatesMenuItem = new ToolStripMenuItem("Check for Updates", null, CheckForUpdates_Click);
        _updateAvailableMenuItem = new ToolStripMenuItem("Update Available", null, OpenLatestRelease_Click)
        {
            Visible = false
        };

        ContextMenuStrip menu = new();
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open Settings", null, OpenSettings_Click));
        menu.Items.Add(_checkForUpdatesMenuItem);
        menu.Items.Add(_updateAvailableMenuItem);
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(new ToolStripMenuItem("Reconnect Discord", null, Reconnect_Click));
        menu.Items.Add(new ToolStripMenuItem("Clear Presence", null, ClearPresence_Click));
        menu.Items.Add(new ToolStripMenuItem("Write Diagnostics Snapshot", null, WriteDiagnostics_Click));
        menu.Items.Add(_startWithWindowsMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, Exit_Click));

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = AppConstants.AppName,
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

        if (_coordinator.Settings.CheckForUpdatesOnStartup)
        {
            _ = CheckForUpdatesAsync(isStartupCheck: true);
        }
    }

    private void WriteDiagnostics_Click(object? sender, EventArgs e)
    {
        try
        {
            QobuzDiagnosticService diagnostics = new();
            string path = diagnostics.WriteSnapshot();

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Failed to write diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
                ? AppConstants.AppName
                : BuildTrayTooltip(track);
        }, null);
    }

    private static string BuildTrayTooltip(TrackSnapshot track)
    {
        string text = $"{AppConstants.AppName}: {track.Artist} - {track.Title}";

        if (text.Length <= AppConstants.NotifyIconMaxTextLength)
        {
            return text;
        }

        return text[..(AppConstants.NotifyIconMaxTextLength - 3)] + "...";
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

    private void CheckForUpdates_Click(object? sender, EventArgs e)
    {
        _ = CheckForUpdatesAsync(isStartupCheck: false);
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

    private async Task CheckForUpdatesAsync(bool isStartupCheck)
    {
        SetUpdateMenuState("Checking for Updates...", enabled: false);

        UpdateCheckResult result = await _updateCheckService.CheckForUpdatesAsync();

        _uiContext.Post(_ =>
        {
            SetUpdateMenuState("Check for Updates", enabled: true);

            if (!string.IsNullOrWhiteSpace(result.ReleaseUrl))
            {
                _latestReleaseUrl = result.ReleaseUrl;
            }

            if (result.IsUpdateAvailable && !string.IsNullOrWhiteSpace(result.LatestVersion))
            {
                _updateAvailableMenuItem.Text = $"Update Available: v{result.LatestVersion}";
                _updateAvailableMenuItem.Visible = true;

                if (isStartupCheck)
                {
                    _notifyIcon.BalloonTipTitle = AppConstants.AppName;
                    _notifyIcon.BalloonTipText = $"Update available: v{result.LatestVersion}";
                    _notifyIcon.ShowBalloonTip(5000);
                }
                else
                {
                    MessageBox.Show(
                        $"Update available: v{result.LatestVersion}{Environment.NewLine}{Environment.NewLine}Current version: v{result.CurrentVersion}",
                        "Update Available",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            if (!isStartupCheck)
            {
                string message = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? $"You're up to date on v{result.CurrentVersion}."
                    : $"Update check failed: {result.ErrorMessage}";

                MessageBox.Show(
                    message,
                    "Check for Updates",
                    MessageBoxButtons.OK,
                    string.IsNullOrWhiteSpace(result.ErrorMessage) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }, null);
    }

    private void OpenLatestRelease_Click(object? sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _latestReleaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Failed to open release page",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SetUpdateMenuState(string text, bool enabled)
    {
        _checkForUpdatesMenuItem.Text = text;
        _checkForUpdatesMenuItem.Enabled = enabled;
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
