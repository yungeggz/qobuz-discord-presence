using System.Diagnostics;
using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class QobuzWindowReader
{
    public WindowTrackInfo? GetCurrentWindowTrackInfo()
    {
        Process? process = Process
            .GetProcessesByName(AppConstants.QobuzProcessName)
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle));

        if (process is null)
        {
            return null;
        }

        return QobuzWindowTitleParser.Parse(process.MainWindowTitle);
    }
}
