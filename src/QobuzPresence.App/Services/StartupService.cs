using Microsoft.Win32;

namespace QobuzPresence.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(AppConstants.StartupRegistryValueName) is not null;
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);

        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            string exePath = Application.ExecutablePath;
            key.SetValue(AppConstants.StartupRegistryValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppConstants.StartupRegistryValueName, false);
        }
    }
}
