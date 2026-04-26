using System.Diagnostics;
using System.Text;
using System.Text.Json;

using QobuzPresence.Services;
using QobuzPresence.UI;

namespace QobuzPresence;

internal static class Program
{
    private const string MutexName = "Local\\QobuzPresence_User";

    [STAThread]
    private static void Main()
    {
        using Mutex mutex = new(false, MutexName);

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

            // ProbeQobuzPlayerPosition();

            Application.Run(context);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void ProbeQobuzPlayerPosition()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string qobuzDirectory = Path.Combine(appData, "Qobuz");

            string? playerFilePath = Directory
                .EnumerateFiles(qobuzDirectory, "player-*.json")
                .OrderBy(path => path)
                .FirstOrDefault();

            if (playerFilePath is null)
            {
                MessageBox.Show("No player-*.json file found.", "Qobuz Probe");
                return;
            }

            using FileStream stream = File.OpenRead(playerFilePath);
            using JsonDocument document = JsonDocument.Parse(stream);

            JsonElement root = document.RootElement;

            StringBuilder output = new();

            output.AppendLine($"Player file: {playerFilePath}");
            output.AppendLine();

            if (TryGetNestedProperty(root, out JsonElement playerData, "player", "data"))
            {
                output.AppendLine("=== player.data keys ===");

                foreach (JsonProperty property in playerData.EnumerateObject())
                {
                    output.AppendLine($"{property.Name}: {DescribeJsonElement(property.Value)}");
                }

                output.AppendLine();

                if (playerData.TryGetProperty("position", out JsonElement position))
                {
                    output.AppendLine("=== player.data.position ===");
                    output.AppendLine(PrettyJson(position));
                    output.AppendLine();
                }
                else
                {
                    output.AppendLine("No player.data.position property found.");
                    output.AppendLine();
                }
            }
            else
            {
                output.AppendLine("No player.data object found.");
                output.AppendLine();
            }

            if (TryGetNestedProperty(root, out JsonElement playqueueData, "playqueue", "data"))
            {
                output.AppendLine("=== playqueue.data summary ===");

                if (playqueueData.TryGetProperty("currentIndex", out JsonElement currentIndexElement))
                {
                    int currentIndex = currentIndexElement.GetInt32();
                    output.AppendLine($"currentIndex: {currentIndex}");

                    if (
                        playqueueData.TryGetProperty("items", out JsonElement items)
                        && items.ValueKind == JsonValueKind.Array
                        && currentIndex >= 0
                        && currentIndex < items.GetArrayLength()
                    )
                    {
                        JsonElement currentItem = items[currentIndex];

                        output.AppendLine("current item:");
                        output.AppendLine(PrettyJson(currentItem));
                    }
                }
            }

            string outputPath = Path.Combine(Path.GetTempPath(), "qobuz-player-position-probe.txt");
            File.WriteAllText(outputPath, output.ToString());

            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{outputPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Qobuz Probe Failed");
        }
    }

    private static bool TryGetNestedProperty(JsonElement root, out JsonElement value, params string[] propertyNames)
    {
        value = root;

        foreach (string propertyName in propertyNames)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            if (!value.TryGetProperty(propertyName, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static string DescribeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => $"object[{element.EnumerateObject().Count()}]",
            JsonValueKind.Array => $"array[{element.GetArrayLength()}]",
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    private static string PrettyJson(JsonElement element)
    {
        return JsonSerializer.Serialize(
            element,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
    }
}
