using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class QobuzTrackReader
{
    public TrackSnapshot? GetTrack(long trackId)
    {
        string? dbPath = QobuzPaths.GetQobuzDatabasePath();

        if (dbPath is null)
        {
            return null;
        }

        try
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            };

            using SqliteConnection connection = new(builder.ToString());
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT *
                FROM L_Track
                WHERE track_id = $trackId OR id = $trackId
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$trackId", trackId);

            using SqliteDataReader reader = command.ExecuteReader(CommandBehavior.SingleRow);

            if (!reader.Read())
            {
                return null;
            }

            return ReadTrack(reader, trackId);
        }
        catch
        {
            return null;
        }
    }

    private static TrackSnapshot? ReadTrack(SqliteDataReader reader, long requestedTrackId)
    {
        string? dataJson = GetString(reader, "data");
        TrackMetadata metadata = ParseTrackMetadata(dataJson);

        string title = FirstNonEmpty(
            metadata.Title,
            GetString(reader, "title"),
            $"Track {requestedTrackId}")!;

        string artist = FirstNonEmpty(
            metadata.Artist,
            GetString(reader, "artist_name"),
            "Unknown Artist")!;

        long trackId = GetInt64(reader, "track_id") ?? requestedTrackId;

        AudioQuality? cachedQuality = GetCachedQuality(reader);
        AudioQuality? metadataQuality = metadata.Quality;
        AudioQuality? quality = cachedQuality ?? metadataQuality;

        TimeSpan? duration = GetDuration(reader) ?? metadata.Duration;

        return new TrackSnapshot(
            trackId,
            title,
            artist,
            quality,
            metadata.CoverImageUrl,
            duration);
    }

    private static AudioQuality? GetCachedQuality(SqliteDataReader reader)
    {
        int? bitDepth = GetInt32(reader, "bit_depth");
        double? samplingRate = GetDouble(reader, "sampling_rate");

        if (bitDepth is null || samplingRate is null)
        {
            return null;
        }

        double samplingRateKhz = NormalizeSamplingRate(samplingRate.Value);
        bool isHiRes = bitDepth.Value > 16 || samplingRateKhz > 44.1;

        return new AudioQuality(bitDepth.Value, samplingRateKhz, 2, isHiRes, "Cached stream");
    }

    private static TimeSpan? GetDuration(SqliteDataReader reader)
    {
        double? seconds = GetDouble(reader, "duration");
        return seconds is null ? null : TimeSpan.FromSeconds(seconds.Value);
    }

    private static TrackMetadata ParseTrackMetadata(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return TrackMetadata.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(dataJson);
            JsonElement root = document.RootElement;

            string? title = TryGetString(root, "title");
            string? artist = TryGetNestedString(root, "performer", "name")
                ?? TryGetNestedString(root, "artist", "name")
                ?? TryGetNestedString(root, "album", "artist", "name");

            TimeSpan? duration = TryGetDouble(root, "duration") is double durationSeconds
                ? TimeSpan.FromSeconds(durationSeconds)
                : null;

            AudioQuality? quality = TryReadQuality(root, "Track metadata")
                ?? TryReadQualityFromChild(root, "album", "Album metadata");

            string? coverImageUrl = TryGetNestedString(root, "album", "assetsAPI", "large")
                ?? TryGetNestedString(root, "album", "assetsAPI", "small")
                ?? TryGetNestedString(root, "album", "image", "large")
                ?? TryGetNestedString(root, "album", "image", "small");

            if (coverImageUrl is not null && coverImageUrl.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
            {
                coverImageUrl = null;
            }

            return new TrackMetadata(title, artist, quality, coverImageUrl, duration);
        }
        catch
        {
            return TrackMetadata.Empty;
        }
    }

    private static AudioQuality? TryReadQualityFromChild(JsonElement root, string childName, string source)
    {
        if (!TryGetProperty(root, childName, out JsonElement child))
        {
            return null;
        }

        return TryReadQuality(child, source);
    }

    private static AudioQuality? TryReadQuality(JsonElement element, string source)
    {
        int? bitDepth = TryGetInt32(element, "maximum_bit_depth") ?? TryGetInt32(element, "bit_depth");
        double? samplingRate = TryGetDouble(element, "maximum_sampling_rate") ?? TryGetDouble(element, "sampling_rate");
        int? channelCount = TryGetInt32(element, "maximum_channel_count") ?? TryGetInt32(element, "channel_count");
        bool hires = TryGetBool(element, "hires") == true || TryGetBool(element, "hires_streamable") == true;

        if (bitDepth is null || samplingRate is null)
        {
            return null;
        }

        double samplingRateKhz = NormalizeSamplingRate(samplingRate.Value);
        bool isHiRes = hires || bitDepth.Value > 16 || samplingRateKhz > 44.1;

        return new AudioQuality(bitDepth.Value, samplingRateKhz, channelCount, isHiRes, source);
    }

    private static double NormalizeSamplingRate(double samplingRate)
    {
        return samplingRate >= 1000 ? samplingRate / 1000 : samplingRate;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? GetString(SqliteDataReader reader, string columnName)
    {
        int ordinal = TryGetOrdinal(reader, columnName);

        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToString(reader.GetValue(ordinal));
    }

    private static int? GetInt32(SqliteDataReader reader, string columnName)
    {
        int ordinal = TryGetOrdinal(reader, columnName);

        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static long? GetInt64(SqliteDataReader reader, string columnName)
    {
        int ordinal = TryGetOrdinal(reader, columnName);

        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt64(reader.GetValue(ordinal));
    }

    private static double? GetDouble(SqliteDataReader reader, string columnName)
    {
        int ordinal = TryGetOrdinal(reader, columnName);

        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToDouble(reader.GetValue(ordinal));
    }

    private static int TryGetOrdinal(SqliteDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? TryGetNestedString(JsonElement element, params string[] path)
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

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)
            ? value
            : null;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double value)
            ? value
            : null;
    }

    private static bool? TryGetBool(JsonElement element, string propertyName)
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

    private sealed record TrackMetadata(
        string? Title,
        string? Artist,
        AudioQuality? Quality,
        string? CoverImageUrl,
        TimeSpan? Duration)
    {
        public static TrackMetadata Empty { get; } = new(null, null, null, null, null);
    }
}
