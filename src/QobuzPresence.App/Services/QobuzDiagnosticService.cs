using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using QobuzPresence.Helpers;
using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class QobuzDiagnosticService
{
    public string WriteSnapshot()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppDataDirectoryName,
            AppConstants.DiagnosticsDirectoryName);

        Directory.CreateDirectory(directory);

        string path = Path.Combine(
            directory,
            $"qobuz-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        string snapshot = BuildSnapshot();

        File.WriteAllText(path, snapshot, Encoding.UTF8);

        return path;
    }

    private static string BuildSnapshot()
    {
        StringBuilder output = new();

        output.AppendLine($"{AppConstants.AppName} Diagnostic Snapshot");
        output.AppendLine($"Created: {DateTimeOffset.Now:O}");
        output.AppendLine();

        string qobuzDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.QobuzDirectoryName);

        string dbPath = Path.Combine(qobuzDirectory, AppConstants.QobuzDatabaseFileName);

        output.AppendLine("=== Environment ===");
        output.AppendLine($"Qobuz directory: {qobuzDirectory}");
        output.AppendLine($"qobuz.db exists: {File.Exists(dbPath)}");
        output.AppendLine();

        AppendQobuzProcesses(output);
        AppendQobuzWindows(output);
        AppendPlayerFiles(output, qobuzDirectory, dbPath);

        return output.ToString();
    }

    private static void AppendQobuzProcesses(StringBuilder output)
    {
        output.AppendLine("=== Qobuz Processes ===");

        Process[] processes = Process.GetProcessesByName(AppConstants.QobuzProcessName);

        try
        {
            if (processes.Length == 0)
            {
                output.AppendLine("No Qobuz.exe processes found.");
                output.AppendLine();
                return;
            }

            foreach (Process process in processes.OrderBy(process => process.Id))
            {
                try
                {
                    output.AppendLine(
                        $"PID={process.Id}, MainWindowTitle=\"{process.MainWindowTitle}\", HasExited={process.HasExited}");
                }
                catch (Exception ex)
                {
                    output.AppendLine($"PID={process.Id}, failed to inspect process: {ex.Message}");
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        output.AppendLine();
    }

    private static void AppendQobuzWindows(StringBuilder output)
    {
        output.AppendLine("=== Visible Qobuz Windows ===");

        List<WindowInfo> windows = EnumerateQobuzWindows();

        if (windows.Count == 0)
        {
            output.AppendLine("No visible Qobuz windows found.");
            output.AppendLine();
            return;
        }

        foreach (WindowInfo window in windows)
        {
            output.AppendLine(
                $"HWND={window.Handle}, PID={window.ProcessId}, Title=\"{window.Title}\"");

            WindowTrackInfo? parsed = QobuzWindowTitleParser.Parse(window.Title);

            output.AppendLine(
                parsed is null
                    ? "  Parsed: null"
                    : $"  Parsed: Title=\"{parsed.Title}\", Artist=\"{parsed.Artist}\"");
        }

        output.AppendLine();
    }

    private static void AppendPlayerFiles(StringBuilder output, string qobuzDirectory, string dbPath)
    {
        output.AppendLine("=== Player Files ===");

        if (!Directory.Exists(qobuzDirectory))
        {
            output.AppendLine("Qobuz directory does not exist.");
            output.AppendLine();
            return;
        }

        string[] playerFiles = Directory
            .EnumerateFiles(qobuzDirectory, AppConstants.PlayerFilePattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        if (playerFiles.Length == 0)
        {
            output.AppendLine("No player-*.json files found.");
            output.AppendLine();
            return;
        }

        List<WindowInfo> windows = EnumerateQobuzWindows();
        WindowTrackInfo? activeWindow = windows
            .Select(window => QobuzWindowTitleParser.Parse(window.Title))
            .FirstOrDefault(parsed => parsed is not null);

        if (activeWindow is not null)
        {
            output.AppendLine(
                $"Active window guess: Title=\"{activeWindow.Title}\", Artist=\"{activeWindow.Artist}\"");
        }
        else
        {
            output.AppendLine("Active window guess: null");
        }

        output.AppendLine();

        foreach (string playerFile in playerFiles)
        {
            AppendPlayerFile(output, playerFile, dbPath, activeWindow);
        }

        output.AppendLine();
    }

    private static void AppendPlayerFile(
        StringBuilder output,
        string playerFile,
        string dbPath,
        WindowTrackInfo? activeWindow)
    {
        FileInfo fileInfo = new(playerFile);

        output.AppendLine("--- Player File ---");
        output.AppendLine($"Path: {playerFile}");
        output.AppendLine($"LastWriteUtc: {fileInfo.LastWriteTimeUtc:O}");
        output.AppendLine($"Size: {fileInfo.Length:N0} bytes");

        try
        {
            using FileStream stream = File.OpenRead(playerFile);
            using JsonDocument document = JsonDocument.Parse(stream);

            JsonElement root = document.RootElement;

            PlayerQueueInfo? queueInfo = TryReadQueueInfo(root);

            if (queueInfo is null)
            {
                output.AppendLine("Queue: null/unreadable");
                output.AppendLine();
                return;
            }

            output.AppendLine($"CurrentIndex: {queueInfo.CurrentIndex}");
            output.AppendLine($"ItemsCount: {queueInfo.ItemsCount}");
            output.AppendLine($"Selected TrackId: {queueInfo.TrackId}");
            output.AppendLine($"Selected QueueItemId: {queueInfo.QueueItemId}");
            output.AppendLine($"Selected CloudItemId: {queueInfo.CloudItemId}");
            output.AppendLine($"PositionMs: {queueInfo.PositionMilliseconds?.ToString() ?? "null"}");
            output.AppendLine($"PositionSeconds: {(queueInfo.PositionMilliseconds.HasValue ? (queueInfo.PositionMilliseconds.Value / 1000.0).ToString("0.###") : "null")}");
            output.AppendLine($"PositionTimestampMs: {queueInfo.PositionTimestampMilliseconds?.ToString() ?? "null"}");

            if (queueInfo.PositionTimestampMilliseconds.HasValue)
            {
                try
                {
                    DateTimeOffset reportedAt = DateTimeOffset.FromUnixTimeMilliseconds(queueInfo.PositionTimestampMilliseconds.Value);
                    output.AppendLine($"PositionReportedAtUtc: {reportedAt:O}");
                    output.AppendLine($"PositionReportedAgeSeconds: {(DateTimeOffset.UtcNow - reportedAt).TotalSeconds:0.###}");
                }
                catch
                {
                    output.AppendLine("PositionReportedAtUtc: invalid timestamp");
                }
            }

            output.AppendLine();

            if (File.Exists(dbPath))
            {
                AppendTrackDatabaseLookup(output, dbPath, queueInfo.TrackId, activeWindow);
                AppendPresenceResolutionPreview(output, queueInfo, activeWindow);
            }
            else
            {
                output.AppendLine("DB lookup skipped. qobuz.db not found.");
            }
        }
        catch (Exception ex)
        {
            output.AppendLine($"Failed to read player file: {ex}");
        }

        output.AppendLine();
    }

    private static PlayerQueueInfo? TryReadQueueInfo(JsonElement root)
    {
        if (!JsonElementHelper.TryGetNestedProperty(root, out JsonElement playqueueData, "playqueue", "data"))
        {
            return null;
        }

        int? currentIndex = JsonElementHelper.GetInt32(playqueueData, "currentIndex");

        if (!currentIndex.HasValue)
        {
            return null;
        }

        if (!JsonElementHelper.TryGetProperty(playqueueData, "items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int itemsCount = items.GetArrayLength();

        if (currentIndex.Value < 0 || currentIndex.Value >= itemsCount)
        {
            return new PlayerQueueInfo(
                currentIndex.Value,
                itemsCount,
                TrackId: -1,
                QueueItemId: null,
                CloudItemId: null,
                PositionMilliseconds: TryReadPositionMilliseconds(root),
                PositionTimestampMilliseconds: TryReadPositionTimestampMilliseconds(root));
        }

        JsonElement currentItem = items[currentIndex.Value];

        long trackId = JsonElementHelper.GetInt64(currentItem, "trackId") ?? -1;
        string? queueItemId = JsonElementHelper.GetString(currentItem, "queueItemId");
        long? cloudItemId = JsonElementHelper.GetInt64(currentItem, "cloudItemId");

        return new PlayerQueueInfo(
            currentIndex.Value,
            itemsCount,
            trackId,
            queueItemId,
            cloudItemId,
            TryReadPositionMilliseconds(root),
            TryReadPositionTimestampMilliseconds(root));
    }

    private static long? TryReadPositionMilliseconds(JsonElement root)
    {
        if (!JsonElementHelper.TryGetNestedProperty(root, out JsonElement position, "player", "data", "position"))
        {
            return null;
        }

        return JsonElementHelper.GetInt64(position, "value");
    }

    private static long? TryReadPositionTimestampMilliseconds(JsonElement root)
    {
        if (!JsonElementHelper.TryGetNestedProperty(root, out JsonElement position, "player", "data", "position"))
        {
            return null;
        }

        return JsonElementHelper.GetInt64(position, "timestamp");
    }

    private static void AppendTrackDatabaseLookup(
        StringBuilder output,
        string dbPath,
        long trackId,
        WindowTrackInfo? activeWindow)
    {
        output.AppendLine("=== DB Lookup For Selected TrackId ===");

        if (trackId <= 0)
        {
            output.AppendLine("TrackId is invalid.");
            return;
        }

        try
        {
            using SqliteConnection connection = new($"Data Source={dbPath};Mode=ReadOnly");
            connection.Open();

            TrackDbInfo? lTrack = QueryLTrack(connection, trackId);
            TrackDbInfo? sTrack = QuerySTrack(connection, trackId);
            TrackDbInfo? ftsTrack = QuerySTrackFts(connection, trackId);

            AppendTrackDbInfo(output, "L_Track", lTrack, activeWindow);
            AppendTrackDbInfo(output, "S_Track", sTrack, activeWindow);
            AppendTrackDbInfo(output, "S_Track_fts", ftsTrack, activeWindow);

            if (activeWindow is not null)
            {
                output.AppendLine("=== Window vs DB Match Guess ===");

                TrackDbInfo? best = lTrack ?? sTrack ?? ftsTrack;

                if (best is null)
                {
                    output.AppendLine("No DB metadata available to compare.");
                }
                else
                {
                    output.AppendLine($"Window Title:  \"{activeWindow.Title}\"");
                    output.AppendLine($"Window Artist: \"{activeWindow.Artist}\"");
                    output.AppendLine($"DB Title:      \"{best.Title}\"");
                    output.AppendLine($"DB Artist:     \"{best.Artist}\"");
                    output.AppendLine($"ArtistMatches: {TrackMatchingUtility.ArtistMatches(best.Artist, activeWindow.Artist)}");
                    output.AppendLine($"TitleMatchStage: {TrackMatchingUtility.GetTitleMatchStage(best.Title, activeWindow.Title)}");
                }
            }
        }
        catch (Exception ex)
        {
            output.AppendLine($"DB lookup failed: {ex}");
        }
    }

    private static void AppendTrackDbInfo(
        StringBuilder output,
        string label,
        TrackDbInfo? info,
        WindowTrackInfo? activeWindow)
    {
        output.AppendLine($"--- {label} ---");

        if (info is null)
        {
            output.AppendLine("No row found.");
            return;
        }

        output.AppendLine($"TrackId: {info.TrackId}");
        output.AppendLine($"Title: {info.Title}");
        output.AppendLine($"Artist: {info.Artist}");
        output.AppendLine($"Album: {info.Album}");
        output.AppendLine($"DurationSeconds: {info.DurationSeconds?.ToString("0.###") ?? "null"}");
        output.AppendLine($"CoverUrl: {info.CoverUrl ?? "null"}");
        output.AppendLine($"CachedBitDepth: {info.CachedBitDepth?.ToString() ?? "null"}");
        output.AppendLine($"CachedSamplingRate: {info.CachedSamplingRate?.ToString("0.###") ?? "null"}");
        output.AppendLine($"MetadataQuality: {info.MetadataQuality ?? "null"}");
        output.AppendLine($"CachedQuality: {info.CachedQuality ?? "null"}");

        if (activeWindow is not null)
        {
            output.AppendLine($"CompareToWindowArtist: {activeWindow.Artist}");
            output.AppendLine($"CompareToWindowTitle: {activeWindow.Title}");
        }
    }

    private static void AppendPresenceResolutionPreview(
        StringBuilder output,
        PlayerQueueInfo queueInfo,
        WindowTrackInfo? activeWindow)
    {
        output.AppendLine("=== Presence Resolution Preview ===");

        if (activeWindow is null)
        {
            output.AppendLine("Resolution skipped. Active window title was not parseable.");
            return;
        }

        QobuzTrackReader trackReader = new();
        PlaybackTiming? playbackTiming = TryBuildPlaybackTiming(queueInfo);
        TrackResolutionResult resolution = QobuzTrackResolutionHelper.Resolve(
            trackReader,
            queueInfo.TrackId,
            playbackTiming,
            activeWindow);

        output.AppendLine($"PresenceResolution: {resolution.Source}");
        output.AppendLine($"ResolvedTrackId: {resolution.Track.TrackId}");
        output.AppendLine($"ResolvedTitle: {resolution.Track.Title}");
        output.AppendLine($"ResolvedArtist: {resolution.Track.Artist}");
        output.AppendLine($"ResolvedCoverUrl: {resolution.Track.CoverImageUrl ?? "null"}");
        output.AppendLine($"ResolvedQuality: {resolution.Track.Quality?.DisplayText ?? "null"}");
        output.AppendLine($"ResolvedDurationSeconds: {resolution.Track.Duration?.TotalSeconds.ToString("0.###") ?? "null"}");

        if (!string.IsNullOrWhiteSpace(resolution.StatusNote))
        {
            output.AppendLine($"ResolutionNotes: {resolution.StatusNote}");
        }
    }

    private static PlaybackTiming? TryBuildPlaybackTiming(PlayerQueueInfo queueInfo)
    {
        if (!queueInfo.PositionMilliseconds.HasValue || !queueInfo.PositionTimestampMilliseconds.HasValue)
        {
            return null;
        }

        try
        {
            TimeSpan currentPosition = TimeSpan.FromMilliseconds(queueInfo.PositionMilliseconds.Value);
            DateTimeOffset reportedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(queueInfo.PositionTimestampMilliseconds.Value);
            return new PlaybackTiming(currentPosition, reportedAtUtc);
        }
        catch
        {
            return null;
        }
    }

    private static TrackDbInfo? QueryLTrack(SqliteConnection connection, long trackId)
    {
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                track_id,
                title,
                data,
                duration,
                sampling_rate,
                bit_depth
            FROM L_Track
            WHERE track_id = $trackId OR id = $trackId
            LIMIT 1
            """;

        command.Parameters.AddWithValue("$trackId", trackId);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        string? dataText = SqliteDataReaderHelper.GetString(reader, "data");
        ParsedTrackMetadata jsonMetadata = QobuzTrackMetadataParser.Parse(dataText);

        double? cachedSamplingRate = SqliteDataReaderHelper.GetDouble(reader, "sampling_rate");
        int? cachedBitDepth = SqliteDataReaderHelper.GetInt32(reader, "bit_depth");

        string? cachedQuality = FormatQuality(
            cachedBitDepth,
            cachedSamplingRate,
            channelCount: 2,
            hires: null);

        return new TrackDbInfo(
            TrackId: SqliteDataReaderHelper.GetInt64(reader, "track_id") ?? trackId,
            Title: jsonMetadata.Title ?? SqliteDataReaderHelper.GetString(reader, "title") ?? "(missing title)",
            Artist: jsonMetadata.Artist ?? "(missing artist)",
            Album: jsonMetadata.AlbumTitle,
            DurationSeconds: jsonMetadata.Duration?.TotalSeconds ?? SqliteDataReaderHelper.GetDouble(reader, "duration"),
            CoverUrl: jsonMetadata.CoverImageUrl,
            CachedBitDepth: cachedBitDepth,
            CachedSamplingRate: cachedSamplingRate,
            MetadataQuality: jsonMetadata.Quality?.DisplayText,
            CachedQuality: cachedQuality);
    }

    private static TrackDbInfo? QuerySTrack(SqliteConnection connection, long trackId)
    {
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                id,
                title,
                track_artists_names,
                release_name,
                release_image_small,
                duration
            FROM S_Track
            WHERE id = $trackId OR rowid = $trackId
            LIMIT 1
            """;

        command.Parameters.AddWithValue("$trackId", trackId);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new TrackDbInfo(
            TrackId: SqliteDataReaderHelper.GetInt64(reader, "id") ?? trackId,
            Title: SqliteDataReaderHelper.GetString(reader, "title") ?? "(missing title)",
            Artist: SqliteDataReaderHelper.GetString(reader, "track_artists_names") ?? "(missing artist)",
            Album: SqliteDataReaderHelper.GetString(reader, "release_name"),
            DurationSeconds: SqliteDataReaderHelper.GetDouble(reader, "duration"),
            CoverUrl: SqliteDataReaderHelper.GetString(reader, "release_image_small"),
            CachedBitDepth: null,
            CachedSamplingRate: null,
            MetadataQuality: null,
            CachedQuality: null);
    }

    private static TrackDbInfo? QuerySTrackFts(SqliteConnection connection, long trackId)
    {
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                rowid,
                title,
                track_artists_names,
                release_name
            FROM S_Track_fts
            WHERE rowid = $trackId
            LIMIT 1
            """;

        command.Parameters.AddWithValue("$trackId", trackId);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new TrackDbInfo(
            TrackId: SqliteDataReaderHelper.GetInt64(reader, "rowid") ?? trackId,
            Title: SqliteDataReaderHelper.GetString(reader, "title") ?? "(missing title)",
            Artist: SqliteDataReaderHelper.GetString(reader, "track_artists_names") ?? "(missing artist)",
            Album: SqliteDataReaderHelper.GetString(reader, "release_name"),
            DurationSeconds: null,
            CoverUrl: null,
            CachedBitDepth: null,
            CachedSamplingRate: null,
            MetadataQuality: null,
            CachedQuality: null);
    }

    private static string? FormatQuality(
        int? bitDepth,
        double? samplingRate,
        int? channelCount,
        bool? hires)
    {
        if (!bitDepth.HasValue || !samplingRate.HasValue)
        {
            return null;
        }

        double rate = samplingRate.Value;

        if (rate >= 1000)
        {
            rate /= 1000;
        }

        string prefix = hires == true || bitDepth.Value > 16 || rate > 44.1
            ? "Hi-Res"
            : "Lossless";

        string channelText = channelCount switch
        {
            2 => "Stereo",
            > 0 => $"{channelCount}ch",
            _ => "Stereo"
        };

        return $"{prefix} • {bitDepth.Value}-bit / {rate:g} kHz • {channelText}";
    }

    private static List<WindowInfo> EnumerateQobuzWindows()
    {
        List<WindowInfo> windows = [];

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            int length = GetWindowTextLength(handle);

            if (length <= 0)
            {
                return true;
            }

            StringBuilder builder = new(length + 1);
            _ = GetWindowText(handle, builder, builder.Capacity);

            string title = builder.ToString();

            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            _ = GetWindowThreadProcessId(handle, out int processId);

            try
            {
                using Process process = Process.GetProcessById(processId);

                if (!string.Equals(process.ProcessName, AppConstants.QobuzProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            windows.Add(new WindowInfo(handle, processId, title));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private sealed record WindowInfo(IntPtr Handle, int ProcessId, string Title);

    private sealed record PlayerQueueInfo(
        int CurrentIndex,
        int ItemsCount,
        long TrackId,
        string? QueueItemId,
        long? CloudItemId,
        long? PositionMilliseconds,
        long? PositionTimestampMilliseconds);

    private sealed record TrackDbInfo(
        long TrackId,
        string Title,
        string Artist,
        string? Album,
        double? DurationSeconds,
        string? CoverUrl,
        int? CachedBitDepth,
        double? CachedSamplingRate,
        string? MetadataQuality,
        string? CachedQuality);

    private delegate bool EnumWindowsDelegate(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder builder, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr handle, out int processId);
}
