using QobuzPresence.Models;
using QobuzPresence.Services;

namespace QobuzPresence.UI;

public sealed class SettingsForm : Form
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly AppSettings _settings;

    private readonly CheckBox _qualityInStateCheckBox = new();
    private readonly CheckBox _qualityInHoverCheckBox = new();
    private readonly CheckBox _startWithWindowsCheckBox = new();
    private readonly CheckBox _checkForUpdatesOnStartupCheckBox = new();
    private readonly TextBox _previewTextBox = new();

    public SettingsForm(SettingsService settingsService, StartupService startupService, AppSettings settings)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _settings = settings;

        Text = "Qobuz Discord Presence Settings";
        Icon = TrayAppContext.LoadTrayIcon();
        Width = 700;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BuildUi();
        LoadSettingsIntoControls();
        RegisterPreviewEvents();
        UpdatePreview();
    }

    private void BuildUi()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        TableLayoutPanel optionsPanel = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 8,
            AutoSize = true
        };

        optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _qualityInStateCheckBox.Text = "Show audio quality in visible status line";
        _qualityInStateCheckBox.AutoSize = true;
        optionsPanel.Controls.Add(_qualityInStateCheckBox, 1, 0);

        _qualityInHoverCheckBox.Text = "Show audio quality in album-art hover text";
        _qualityInHoverCheckBox.AutoSize = true;
        optionsPanel.Controls.Add(_qualityInHoverCheckBox, 1, 1);

        _startWithWindowsCheckBox.Text = "Start with Windows";
        _startWithWindowsCheckBox.AutoSize = true;
        optionsPanel.Controls.Add(_startWithWindowsCheckBox, 1, 6);

        _checkForUpdatesOnStartupCheckBox.Text = "Check for updates on startup";
        _checkForUpdatesOnStartupCheckBox.AutoSize = true;
        optionsPanel.Controls.Add(_checkForUpdatesOnStartupCheckBox, 1, 7);

        GroupBox previewGroup = new()
        {
            Text = "Discord Activity Preview",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        _previewTextBox.Multiline = true;
        _previewTextBox.ReadOnly = true;
        _previewTextBox.Dock = DockStyle.Fill;
        _previewTextBox.ScrollBars = ScrollBars.Vertical;
        _previewTextBox.Font = new Font(FontFamily.GenericMonospace, 9);
        previewGroup.Controls.Add(_previewTextBox);

        FlowLayoutPanel buttons = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        Button saveButton = new()
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 90
        };
        saveButton.Click += SaveButton_Click;

        Button cancelButton = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 90
        };

        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        root.Controls.Add(optionsPanel, 0, 0);
        root.Controls.Add(previewGroup, 0, 1);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
    }

    private void LoadSettingsIntoControls()
    {
        _qualityInStateCheckBox.Checked = _settings.DisplayAudioQualityInState;
        _qualityInHoverCheckBox.Checked = _settings.DisplayAudioQualityInLargeImageHover;
        _startWithWindowsCheckBox.Checked = _settings.StartWithWindows || _startupService.IsEnabled();
        _checkForUpdatesOnStartupCheckBox.Checked = _settings.CheckForUpdatesOnStartup;
    }

    private void RegisterPreviewEvents()
    {
        _qualityInStateCheckBox.CheckedChanged += (_, _) => UpdatePreview();
        _qualityInHoverCheckBox.CheckedChanged += (_, _) => UpdatePreview();
        _startWithWindowsCheckBox.CheckedChanged += (_, _) => UpdatePreview();
        _checkForUpdatesOnStartupCheckBox.CheckedChanged += (_, _) => UpdatePreview();
    }

    private void UpdatePreview()
    {
        const string sampleTitle = "Track Title";
        const string sampleArtist = "Track Artist";
        const string sampleQuality = "Hi-Res • 24-bit / 192 kHz • Stereo";

        string state = sampleArtist;

        if (_qualityInStateCheckBox.Checked)
        {
            state += $" • {sampleQuality}";
        }

        string largeHover = $"{sampleTitle} - {sampleArtist}";

        if (_qualityInHoverCheckBox.Checked)
        {
            largeHover += $" • {sampleQuality}";
        }

        _previewTextBox.Text =
            $"{DiscordApplication.DisplayName}{Environment.NewLine}" +
            $"{sampleTitle}{Environment.NewLine}" +
            $"{state}{Environment.NewLine}" +
            $"3:04 [Track countdown, synced to Qobuz playback position]{Environment.NewLine}" +
            $"{Environment.NewLine}" +
            $"<Album Art Display via Qobuz/iTunes>{Environment.NewLine}" +
            $"Album Art Hover Text: {largeHover}{Environment.NewLine}";
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _settings.DisplayAudioQualityInState = _qualityInStateCheckBox.Checked;
        _settings.DisplayAudioQualityInLargeImageHover = _qualityInHoverCheckBox.Checked;
        _settings.StartWithWindows = _startWithWindowsCheckBox.Checked;
        _settings.CheckForUpdatesOnStartup = _checkForUpdatesOnStartupCheckBox.Checked;

        _settingsService.Save(_settings);
        _startupService.SetEnabled(_settings.StartWithWindows);
    }
}
