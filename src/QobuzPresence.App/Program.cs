using QobuzPresence.Services;
using QobuzPresence.UI;

namespace QobuzPresence;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using Mutex mutex = new(false, AppConstants.MutexName);

        if (!mutex.WaitOne(TimeSpan.Zero, true))
        {
            return;
        }

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            SettingsService settingsService = new();
            StartupService startupService = new();
            QobuzStateReader stateReader = new();
            QobuzTrackReader trackReader = new();
            QobuzWindowReader windowReader = new();
            DiscordPresenceService discordService = new();

            using TrayAppContext context = new(
                settingsService,
                startupService,
                stateReader,
                trackReader,
                windowReader,
                discordService);

            Application.Run(context);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
