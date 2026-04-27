namespace QobuzPresence.Services;

public static class QobuzPaths
{
    public static string? GetQobuzRoamingDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrWhiteSpace(appData))
        {
            return null;
        }

        string path = Path.Combine(appData, AppConstants.QobuzDirectoryName);
        return Directory.Exists(path) ? path : null;
    }

    public static string? GetQobuzDatabasePath()
    {
        string? directory = GetQobuzRoamingDirectory();

        if (directory is null)
        {
            return null;
        }

        string path = Path.Combine(directory, AppConstants.QobuzDatabaseFileName);
        return File.Exists(path) ? path : null;
    }
}
