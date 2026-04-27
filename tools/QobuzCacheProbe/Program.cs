using System.Text.Json;
using Microsoft.Data.Sqlite;

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  QobuzCacheProbe <title> [artist]");
    Console.WriteLine("  QobuzCacheProbe --list-tables");
    Console.WriteLine("  QobuzCacheProbe --table-info <table>");
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
        string name = reader.GetString(0);
        Console.WriteLine(name);
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
        count++;

        string? data = ReadString(reader, "data");
        JsonTrackMetadata metadata = ParseMetadata(data);
        string resolvedArtist = FirstNonEmpty(
            metadata.Artist,
            "(missing artist)")!;

        if (!ArtistMatches(resolvedArtist, artist))
        {
            continue;
        }

        Console.WriteLine($"track_id={ReadInt64(reader, "track_id")}, id={ReadInt64(reader, "id")}");
        Console.WriteLine($"title={FirstNonEmpty(metadata.Title, ReadString(reader, "title"), "(missing title)")}");
        Console.WriteLine($"artist={resolvedArtist}");
        Console.WriteLine($"album_id={ReadString(reader, "album_id") ?? "null"}");
        Console.WriteLine($"album_title={metadata.AlbumTitle ?? "null"}");
        Console.WriteLine($"added_date={ReadString(reader, "added_date") ?? "null"}");
        Console.WriteLine($"duration={FirstNonEmpty(metadata.DurationSeconds?.ToString(), ReadDouble(reader, "duration")?.ToString(), "null")}");
        Console.WriteLine($"cover={metadata.CoverUrl ?? "null"}");
        Console.WriteLine($"metadata_quality={metadata.Quality ?? "null"}");
        Console.WriteLine($"cached_quality={FormatQuality(ReadInt32(reader, "bit_depth"), ReadDouble(reader, "sampling_rate"), 2, null) ?? "null"}");
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
        count++;

        string resolvedArtist = ReadString(reader, "track_artists_names") ?? "(missing artist)";

        if (!ArtistMatches(resolvedArtist, artist))
        {
            continue;
        }

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
        count++;

        string resolvedArtist = ReadString(reader, "track_artists_names") ?? "(missing artist)";

        if (!ArtistMatches(resolvedArtist, artist))
        {
            continue;
        }

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

static string? FirstNonEmpty(params string?[] values)
{
    return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

static bool ArtistMatches(string candidateArtist, string? requestedArtist)
{
    if (string.IsNullOrWhiteSpace(requestedArtist))
    {
        return true;
    }

    string normalizedCandidate = Normalize(candidateArtist);
    string normalizedRequested = Normalize(requestedArtist);

    return normalizedCandidate == normalizedRequested ||
        normalizedCandidate.Contains(normalizedRequested, StringComparison.Ordinal) ||
        normalizedRequested.Contains(normalizedCandidate, StringComparison.Ordinal);
}

static string Normalize(string value)
{
    return value.Trim().ToLowerInvariant();
}

static JsonTrackMetadata ParseMetadata(string? data)
{
    if (string.IsNullOrWhiteSpace(data))
    {
        return JsonTrackMetadata.Empty;
    }

    try
    {
        using JsonDocument document = JsonDocument.Parse(data);
        JsonElement root = document.RootElement;

        string? title = TryGetString(root, "title");
        string? artist = TryGetNestedString(root, "performer", "name")
            ?? TryGetNestedString(root, "artist", "name")
            ?? TryGetNestedString(root, "album", "artist", "name");
        string? albumTitle = TryGetNestedString(root, "album", "title");

        string? cover = TryGetNestedString(root, "album", "assetsAPI", "large")
            ?? TryGetNestedString(root, "album", "assetsAPI", "small")
            ?? TryGetNestedString(root, "album", "image", "large")
            ?? TryGetNestedString(root, "album", "image", "small");

        string? quality = FormatQuality(
            TryGetInt32(root, "maximum_bit_depth") ?? TryGetInt32(root, "bit_depth"),
            TryGetDouble(root, "maximum_sampling_rate") ?? TryGetDouble(root, "sampling_rate"),
            TryGetInt32(root, "maximum_channel_count") ?? TryGetInt32(root, "channel_count"),
            TryGetBool(root, "hires") == true || TryGetBool(root, "hires_streamable") == true);

        quality ??= FormatQuality(
            TryGetInt32FromPath(root, "album", "maximum_bit_depth") ?? TryGetInt32FromPath(root, "album", "bit_depth"),
            TryGetDoubleFromPath(root, "album", "maximum_sampling_rate") ?? TryGetDoubleFromPath(root, "album", "sampling_rate"),
            TryGetInt32FromPath(root, "album", "maximum_channel_count") ?? TryGetInt32FromPath(root, "album", "channel_count"),
            TryGetBoolFromPath(root, "album", "hires") == true || TryGetBoolFromPath(root, "album", "hires_streamable") == true);

        double? duration = TryGetDouble(root, "duration");

        return new JsonTrackMetadata(title, artist, albumTitle, cover, quality, duration);
    }
    catch
    {
        return JsonTrackMetadata.Empty;
    }
}

static string? FormatQuality(int? bitDepth, double? samplingRate, int? channelCount, bool? hires)
{
    if (!bitDepth.HasValue || !samplingRate.HasValue)
    {
        return null;
    }

    double rate = samplingRate.Value >= 1000
        ? samplingRate.Value / 1000
        : samplingRate.Value;

    string prefix = hires == true || bitDepth.Value > 16 || rate > 44.1
        ? "Hi-Res"
        : "Lossless";

    string channels = channelCount switch
    {
        2 => "Stereo",
        > 0 => $"{channelCount}ch",
        _ => "Stereo"
    };

    return $"{prefix} • {bitDepth.Value}-bit / {rate:g} kHz • {channels}";
}

static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
{
    if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
    {
        return true;
    }

    property = default;
    return false;
}

static string? TryGetString(JsonElement element, string propertyName)
{
    return TryGetProperty(element, propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;
}

static string? TryGetNestedString(JsonElement element, params string[] path)
{
    JsonElement current = element;

    foreach (string segment in path)
    {
        if (!TryGetProperty(current, segment, out current))
        {
            return null;
        }
    }

    return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
}

static int? TryGetInt32(JsonElement element, string propertyName)
{
    if (!TryGetProperty(element, propertyName, out JsonElement property))
    {
        return null;
    }

    return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)
        ? value
        : null;
}

static int? TryGetInt32FromPath(JsonElement element, params string[] path)
{
    JsonElement current = element;

    foreach (string segment in path[..^1])
    {
        if (!TryGetProperty(current, segment, out current))
        {
            return null;
        }
    }

    return TryGetInt32(current, path[^1]);
}

static double? TryGetDouble(JsonElement element, string propertyName)
{
    if (!TryGetProperty(element, propertyName, out JsonElement property))
    {
        return null;
    }

    return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double value)
        ? value
        : null;
}

static double? TryGetDoubleFromPath(JsonElement element, params string[] path)
{
    JsonElement current = element;

    foreach (string segment in path[..^1])
    {
        if (!TryGetProperty(current, segment, out current))
        {
            return null;
        }
    }

    return TryGetDouble(current, path[^1]);
}

static bool? TryGetBool(JsonElement element, string propertyName)
{
    if (!TryGetProperty(element, propertyName, out JsonElement property))
    {
        return null;
    }

    return property.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}

static bool? TryGetBoolFromPath(JsonElement element, params string[] path)
{
    JsonElement current = element;

    foreach (string segment in path[..^1])
    {
        if (!TryGetProperty(current, segment, out current))
        {
            return null;
        }
    }

    return TryGetBool(current, path[^1]);
}

sealed record JsonTrackMetadata(
    string? Title,
    string? Artist,
    string? AlbumTitle,
    string? CoverUrl,
    string? Quality,
    double? DurationSeconds)
{
    public static JsonTrackMetadata Empty { get; } = new(null, null, null, null, null, null);
}
