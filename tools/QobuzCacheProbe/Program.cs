using Microsoft.Data.Sqlite;
using QobuzPresence.Helpers;
using QobuzPresence.Models;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Qobuz",
    "qobuz.db");

if (!File.Exists(dbPath))
{
    Console.WriteLine($"qobuz.db not found: {dbPath}");
    return 1;
}

using SqliteConnection connection = new(new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadOnly,
    Cache = SqliteCacheMode.Shared
}.ToString());

connection.Open();

if (args[0] == "--list-tables")
{
    ListTables(connection);
    return 0;
}

if (args[0] == "--table-info")
{
    if (args.Length < 2)
    {
        Console.WriteLine("Missing table name.");
        return 1;
    }

    DumpTableInfo(connection, args[1]);
    return 0;
}

if (args[0] == "--match-titles")
{
    if (args.Length < 3)
    {
        Console.WriteLine("Usage: QobuzCacheProbe --match-titles <dbTitle> <windowTitle>");
        return 1;
    }

    DumpTitleMatch(args[1], args[2]);
    return 0;
}

if (args[0] == "--track-id")
{
    return HandleTrackIdMode(connection, dbPath, args);
}

string title = args[0];
string? artist = args.Length > 1 ? args[1] : null;

Console.WriteLine($"DB: {dbPath}");
Console.WriteLine($"Title: {title}");
Console.WriteLine($"Artist: {artist ?? "(null)"}");
Console.WriteLine();

DumpLTrackMatches(connection, title, artist);
DumpSTrackMatches(connection, title, artist);
DumpSTrackFtsMatches(connection, title, artist);

return 0;

static int HandleTrackIdMode(SqliteConnection connection, string dbPath, string[] args)
{
    if (args.Length < 4)
    {
        Console.WriteLine("Usage: QobuzCacheProbe --track-id <id> --window-title <title> [--window-artist <artist>]");
        return 1;
    }

    if (!long.TryParse(args[1], out long trackId))
    {
        Console.WriteLine($"Invalid track id: {args[1]}");
        return 1;
    }

    string? windowTitle = null;
    string? windowArtist = null;

    for (int i = 2; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--window-title":
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("Missing value for --window-title.");
                    return 1;
                }

                windowTitle = args[++i];
                break;

            case "--window-artist":
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("Missing value for --window-artist.");
                    return 1;
                }

                windowArtist = args[++i];
                break;

            default:
                Console.WriteLine($"Unknown argument: {args[i]}");
                return 1;
        }
    }

    if (string.IsNullOrWhiteSpace(windowTitle))
    {
        Console.WriteLine("Missing required --window-title.");
        return 1;
    }

    Console.WriteLine($"DB: {dbPath}");
    Console.WriteLine($"TrackId: {trackId}");
    Console.WriteLine($"WindowTitle: {windowTitle}");
    Console.WriteLine($"WindowArtist: {windowArtist ?? "(null)"}");
    Console.WriteLine();

    TrackDbRow? lTrack = QueryLTrackByTrackId(connection, trackId);
    TrackDbRow? sTrack = QuerySTrackByTrackId(connection, trackId);
    TrackDbRow? ftsTrack = QuerySTrackFtsByTrackId(connection, trackId);

    DumpTrackRow("L_Track", lTrack);
    DumpTrackRow("S_Track", sTrack);
    DumpTrackRow("S_Track_fts", ftsTrack);

    TrackDbRow? best = lTrack ?? sTrack ?? ftsTrack;

    Console.WriteLine("=== Selected Track Resolver Preview ===");

    if (best is null)
    {
        Console.WriteLine("No DB row found for track id.");
        return 0;
    }

    bool artistMatches = TrackMatchingUtility.ArtistMatches(best.Artist, windowArtist);
    TrackTitleMatchStage titleStage = TrackMatchingUtility.GetTitleMatchStage(best.Title, windowTitle);

    Console.WriteLine($"ResolvedDbTitle: {best.Title}");
    Console.WriteLine($"ResolvedDbArtist: {best.Artist}");
    Console.WriteLine($"ArtistMatches: {artistMatches}");
    Console.WriteLine($"TitleMatchStage: {titleStage}");
    Console.WriteLine($"ShouldPreferWindowTitle: {TrackMatchingUtility.ShouldPreferWindowTitle(best.Title, windowTitle)}");
    Console.WriteLine($"WouldUseSelectedTrackMetadata: {artistMatches && titleStage is not TrackTitleMatchStage.None}");
    Console.WriteLine($"CoverUrl: {best.CoverUrl ?? "null"}");
    Console.WriteLine($"Quality: {best.QualityText ?? "null"}");
    Console.WriteLine($"DurationSeconds: {best.DurationSeconds?.ToString() ?? "null"}");

    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  QobuzCacheProbe <title> [artist]");
    Console.WriteLine("  QobuzCacheProbe --track-id <id> --window-title <title> [--window-artist <artist>]");
    Console.WriteLine("  QobuzCacheProbe --match-titles <dbTitle> <windowTitle>");
    Console.WriteLine("  QobuzCacheProbe --list-tables");
    Console.WriteLine("  QobuzCacheProbe --table-info <table>");
}

static void ListTables(SqliteConnection connection)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT name
        FROM sqlite_master
        WHERE type = 'table'
        ORDER BY name
        """;

    using SqliteDataReader reader = command.ExecuteReader();

    while (reader.Read())
    {
        Console.WriteLine(reader.GetString(0));
    }
}

static void DumpTableInfo(SqliteConnection connection, string tableName)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";

    using SqliteDataReader reader = command.ExecuteReader();

    while (reader.Read())
    {
        Console.WriteLine($"{reader.GetValue(0)}|{reader.GetValue(1)}|{reader.GetValue(2)}");
    }
}

static void DumpTitleMatch(string dbTitle, string windowTitle)
{
    Console.WriteLine($"DbTitle: {dbTitle}");
    Console.WriteLine($"WindowTitle: {windowTitle}");
    Console.WriteLine($"NormalizedDbTitle: {TextUtility.NormalizeForComparison(dbTitle)}");
    Console.WriteLine($"NormalizedWindowTitle: {TextUtility.NormalizeForComparison(windowTitle)}");
    Console.WriteLine($"StrippedDbTitle: {TextUtility.StripBracketedDecorations(dbTitle)}");
    Console.WriteLine($"StrippedWindowTitle: {TextUtility.StripBracketedDecorations(windowTitle)}");
    Console.WriteLine($"TitleMatchStage: {TrackMatchingUtility.GetTitleMatchStage(dbTitle, windowTitle)}");
    Console.WriteLine($"ShouldPreferWindowTitle: {TrackMatchingUtility.ShouldPreferWindowTitle(dbTitle, windowTitle)}");
}

static void DumpLTrackMatches(SqliteConnection connection, string title, string? artist)
{
    Console.WriteLine("=== L_Track exact-title candidates ===");

    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT *
        FROM L_Track
        WHERE title = $title COLLATE NOCASE
        ORDER BY track_id
        """;
    command.Parameters.AddWithValue("$title", title);

    using SqliteDataReader reader = command.ExecuteReader();

    int count = 0;

    while (reader.Read())
    {
        string? data = ReadString(reader, "data");
        ParsedTrackMetadata metadata = QobuzTrackMetadataParser.Parse(data);
        string resolvedArtist = TextUtility.FirstNonEmpty(
            metadata.Artist,
            "(missing artist)")!;

        if (!TrackMatchingUtility.ArtistMatches(resolvedArtist, artist))
        {
            continue;
        }

        count++;

        Console.WriteLine($"track_id={ReadInt64(reader, "track_id")}, id={ReadInt64(reader, "id")}");
        Console.WriteLine($"title={TextUtility.FirstNonEmpty(metadata.Title, ReadString(reader, "title"), "(missing title)")}");
        Console.WriteLine($"artist={resolvedArtist}");
        Console.WriteLine($"album_id={ReadString(reader, "album_id") ?? "null"}");
        Console.WriteLine($"album_title={metadata.AlbumTitle ?? "null"}");
        Console.WriteLine($"added_date={ReadString(reader, "added_date") ?? "null"}");
        Console.WriteLine($"duration={TextUtility.FirstNonEmpty(metadata.Duration?.TotalSeconds.ToString(), ReadDouble(reader, "duration")?.ToString(), "null")}");
        Console.WriteLine($"cover={metadata.CoverImageUrl ?? "null"}");
        Console.WriteLine($"metadata_quality={metadata.Quality?.DisplayText ?? "null"}");
        Console.WriteLine($"cached_quality={BuildCachedQualityText(ReadInt32(reader, "bit_depth"), ReadDouble(reader, "sampling_rate"), 2, null) ?? "null"}");
        Console.WriteLine();
    }

    if (count == 0)
    {
        Console.WriteLine("No exact title matches.");
        Console.WriteLine();
    }
}

static void DumpSTrackMatches(SqliteConnection connection, string title, string? artist)
{
    Console.WriteLine("=== S_Track exact-title candidates ===");

    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT id, title, track_artists_names, release_name, release_image_small, duration
        FROM S_Track
        WHERE title = $title COLLATE NOCASE
        ORDER BY id
        """;
    command.Parameters.AddWithValue("$title", title);

    using SqliteDataReader reader = command.ExecuteReader();

    int count = 0;

    while (reader.Read())
    {
        string resolvedArtist = ReadString(reader, "track_artists_names") ?? "(missing artist)";

        if (!TrackMatchingUtility.ArtistMatches(resolvedArtist, artist))
        {
            continue;
        }

        count++;

        Console.WriteLine($"id={ReadInt64(reader, "id")}");
        Console.WriteLine($"title={ReadString(reader, "title") ?? "(missing title)"}");
        Console.WriteLine($"artist={resolvedArtist}");
        Console.WriteLine($"release_id={TryReadString(reader, "release_id") ?? "null"}");
        Console.WriteLine($"album={ReadString(reader, "release_name") ?? "null"}");
        Console.WriteLine($"cover={ReadString(reader, "release_image_small") ?? "null"}");
        Console.WriteLine($"duration={ReadDouble(reader, "duration")?.ToString() ?? "null"}");
        Console.WriteLine();
    }

    if (count == 0)
    {
        Console.WriteLine("No exact title matches.");
        Console.WriteLine();
    }
}

static void DumpSTrackFtsMatches(SqliteConnection connection, string title, string? artist)
{
    Console.WriteLine("=== S_Track_fts exact-title candidates ===");

    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT rowid, title, track_artists_names, release_name
        FROM S_Track_fts
        WHERE title = $title COLLATE NOCASE
        ORDER BY rowid
        """;
    command.Parameters.AddWithValue("$title", title);

    using SqliteDataReader reader = command.ExecuteReader();

    int count = 0;

    while (reader.Read())
    {
        string resolvedArtist = ReadString(reader, "track_artists_names") ?? "(missing artist)";

        if (!TrackMatchingUtility.ArtistMatches(resolvedArtist, artist))
        {
            continue;
        }

        count++;

        Console.WriteLine($"rowid={ReadInt64(reader, "rowid")}");
        Console.WriteLine($"title={ReadString(reader, "title") ?? "(missing title)"}");
        Console.WriteLine($"artist={resolvedArtist}");
        Console.WriteLine($"album={ReadString(reader, "release_name") ?? "null"}");
        Console.WriteLine();
    }

    if (count == 0)
    {
        Console.WriteLine("No exact title matches.");
        Console.WriteLine();
    }
}

static void DumpTrackRow(string label, TrackDbRow? row)
{
    Console.WriteLine($"=== {label} by track id ===");

    if (row is null)
    {
        Console.WriteLine("No row found.");
        Console.WriteLine();
        return;
    }

    Console.WriteLine($"track_id={row.TrackId}");
    Console.WriteLine($"title={row.Title}");
    Console.WriteLine($"artist={row.Artist}");
    Console.WriteLine($"album={row.AlbumTitle ?? "null"}");
    Console.WriteLine($"cover={row.CoverUrl ?? "null"}");
    Console.WriteLine($"quality={row.QualityText ?? "null"}");
    Console.WriteLine($"duration={row.DurationSeconds?.ToString() ?? "null"}");
    Console.WriteLine();
}

static TrackDbRow? QueryLTrackByTrackId(SqliteConnection connection, long trackId)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT *
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

    ParsedTrackMetadata metadata = QobuzTrackMetadataParser.Parse(ReadString(reader, "data"));

    return new TrackDbRow(
        ReadInt64(reader, "track_id") ?? trackId,
        TextUtility.FirstNonEmpty(metadata.Title, ReadString(reader, "title"), "(missing title)")!,
        TextUtility.FirstNonEmpty(metadata.Artist, ReadString(reader, "artist_name"), "(missing artist)")!,
        metadata.AlbumTitle,
        metadata.CoverImageUrl,
        metadata.Quality?.DisplayText ?? BuildCachedQualityText(ReadInt32(reader, "bit_depth"), ReadDouble(reader, "sampling_rate"), 2, null),
        metadata.Duration?.TotalSeconds ?? ReadDouble(reader, "duration"));
}

static TrackDbRow? QuerySTrackByTrackId(SqliteConnection connection, long trackId)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT id, title, track_artists_names, release_name, release_image_small, duration
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

    return new TrackDbRow(
        ReadInt64(reader, "id") ?? trackId,
        ReadString(reader, "title") ?? "(missing title)",
        ReadString(reader, "track_artists_names") ?? "(missing artist)",
        ReadString(reader, "release_name"),
        ReadString(reader, "release_image_small"),
        null,
        ReadDouble(reader, "duration"));
}

static TrackDbRow? QuerySTrackFtsByTrackId(SqliteConnection connection, long trackId)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT rowid, title, track_artists_names, release_name
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

    return new TrackDbRow(
        ReadInt64(reader, "rowid") ?? trackId,
        ReadString(reader, "title") ?? "(missing title)",
        ReadString(reader, "track_artists_names") ?? "(missing artist)",
        ReadString(reader, "release_name"),
        null,
        null,
        null);
}

static string? ReadString(SqliteDataReader reader, string columnName)
{
    int ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));
}

static string? TryReadString(SqliteDataReader reader, string columnName)
{
    try
    {
        return ReadString(reader, columnName);
    }
    catch (ArgumentOutOfRangeException)
    {
        return null;
    }
}

static long? ReadInt64(SqliteDataReader reader, string columnName)
{
    int ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal));
}

static int? ReadInt32(SqliteDataReader reader, string columnName)
{
    int ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
}

static double? ReadDouble(SqliteDataReader reader, string columnName)
{
    int ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal));
}

static string? BuildCachedQualityText(int? bitDepth, double? samplingRate, int? channelCount, bool? hires)
{
    if (!bitDepth.HasValue || !samplingRate.HasValue)
    {
        return null;
    }

    double samplingRateKhz = samplingRate.Value >= 1000
        ? samplingRate.Value / 1000
        : samplingRate.Value;

    bool isHiRes = hires == true || bitDepth.Value > 16 || samplingRateKhz > 44.1;
    return new AudioQuality(bitDepth.Value, samplingRateKhz, channelCount, isHiRes, "Probe cache").DisplayText;
}

sealed record TrackDbRow(
    long TrackId,
    string Title,
    string Artist,
    string? AlbumTitle,
    string? CoverUrl,
    string? QualityText,
    double? DurationSeconds);
