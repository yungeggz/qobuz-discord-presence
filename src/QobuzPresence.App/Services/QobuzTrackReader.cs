using System.Data;
using Microsoft.Data.Sqlite;
using QobuzPresence.Helpers;
using QobuzPresence.Models;

namespace QobuzPresence.Services;

public sealed class QobuzTrackReader
{
    public TrackSnapshot? GetTrack(long trackId)
    {
        return GetTrackLookup(trackId)?.Track;
    }

    public TrackSnapshot? FindByTitleAndArtist(string title, string? artist)
    {
        return FindByTitleAndArtistLookup(title, artist)?.Track;
    }

    internal TrackLookupResult? GetTrackLookup(long trackId)
    {
        try
        {
            using SqliteConnection connection = OpenReadOnlyConnection();
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

            TrackSnapshot? track = ReadTrack(reader, trackId);
            return track is null ? null : new TrackLookupResult(track, TrackLookupSource.LTrackById);
        }
        catch
        {
            return null;
        }
    }

    internal TrackLookupResult? FindByTitleAndArtistLookup(string title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        try
        {
            using SqliteConnection connection = OpenReadOnlyConnection();
            connection.Open();

            TrackLookupResult? lTrackMatch = FindLTrackByTitleAndArtist(connection, title, artist);

            if (lTrackMatch is not null)
            {
                return lTrackMatch;
            }

            return FindSTrackByTitleAndArtist(connection, title, artist);
        }
        catch
        {
            return null;
        }
    }

    private static SqliteConnection OpenReadOnlyConnection()
    {
        string? dbPath = QobuzPaths.GetQobuzDatabasePath();

        if (dbPath is null)
        {
            throw new InvalidOperationException("Qobuz database path not found.");
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };

        return new SqliteConnection(builder.ToString());
    }

    private static TrackSnapshot? ReadTrack(SqliteDataReader reader, long requestedTrackId)
    {
        string? dataJson = SqliteDataReaderHelper.GetString(reader, "data");
        ParsedTrackMetadata metadata = QobuzTrackMetadataParser.Parse(dataJson);

        string title = TextUtility.FirstNonEmpty(
            metadata.Title,
            SqliteDataReaderHelper.GetString(reader, "title"),
            $"Track {requestedTrackId}")!;

        string artist = TextUtility.FirstNonEmpty(
            metadata.Artist,
            SqliteDataReaderHelper.GetString(reader, "artist_name"),
            AppConstants.UnknownArtist)!;

        long trackId = SqliteDataReaderHelper.GetInt64(reader, "track_id") ?? requestedTrackId;

        AudioQuality? cachedQuality = GetCachedQuality(reader);
        AudioQuality? quality = cachedQuality ?? metadata.Quality;
        TimeSpan? duration = GetDuration(reader) ?? metadata.Duration;

        return new TrackSnapshot(
            trackId,
            title,
            artist,
            quality,
            metadata.CoverImageUrl,
            duration);
    }

    private static TrackLookupResult? FindLTrackByTitleAndArtist(SqliteConnection connection, string title, string? artist)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM L_Track
            WHERE title = $title COLLATE NOCASE
            ORDER BY added_date DESC, id DESC
            """;
        command.Parameters.AddWithValue("$title", title);

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            TrackSnapshot? track = ReadTrack(
                reader,
                SqliteDataReaderHelper.GetInt64(reader, "track_id") ?? SqliteDataReaderHelper.GetInt64(reader, "id") ?? 0);

            if (track is not null && ArtistMatches(track.Artist, artist))
            {
                return new TrackLookupResult(track, TrackLookupSource.LTrackByTitleAndArtist);
            }
        }

        return null;
    }

    private static TrackLookupResult? FindSTrackByTitleAndArtist(SqliteConnection connection, string title, string? artist)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                title,
                track_artists_names,
                release_image_small,
                duration
            FROM S_Track
            WHERE title = $title COLLATE NOCASE
            ORDER BY id DESC
            """;
        command.Parameters.AddWithValue("$title", title);

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            string candidateArtist = TextUtility.FirstNonEmpty(
                SqliteDataReaderHelper.GetString(reader, "track_artists_names"),
                AppConstants.UnknownArtist)!;

            if (!ArtistMatches(candidateArtist, artist))
            {
                continue;
            }

            long trackId = SqliteDataReaderHelper.GetInt64(reader, "id") ?? 0;

            TrackSnapshot track = new(
                trackId,
                TextUtility.FirstNonEmpty(SqliteDataReaderHelper.GetString(reader, "title"), title)!,
                candidateArtist,
                null,
                SqliteDataReaderHelper.GetString(reader, "release_image_small"),
                GetDuration(reader));

            return new TrackLookupResult(track, TrackLookupSource.STrackByTitleAndArtist);
        }

        return null;
    }

    private static bool ArtistMatches(string candidateArtist, string? requestedArtist)
    {
        if (string.IsNullOrWhiteSpace(requestedArtist))
        {
            return true;
        }

        return TextUtility.IsEquivalentOrContained(candidateArtist, requestedArtist);
    }

    private static AudioQuality? GetCachedQuality(SqliteDataReader reader)
    {
        int? bitDepth = SqliteDataReaderHelper.GetInt32(reader, "bit_depth");
        double? samplingRate = SqliteDataReaderHelper.GetDouble(reader, "sampling_rate");

        if (bitDepth is null || samplingRate is null)
        {
            return null;
        }

        double samplingRateKhz = NormalizeSamplingRate(samplingRate.Value);
        bool isHiRes = bitDepth.Value > 16 || samplingRateKhz > 44.1;

        return new AudioQuality(
            bitDepth.Value,
            samplingRateKhz,
            2,
            isHiRes,
            AppConstants.CachedStreamQualitySource);
    }

    private static TimeSpan? GetDuration(SqliteDataReader reader)
    {
        double? seconds = SqliteDataReaderHelper.GetDouble(reader, "duration");
        return seconds is null ? null : TimeSpan.FromSeconds(seconds.Value);
    }

    private static double NormalizeSamplingRate(double samplingRate)
    {
        return samplingRate >= 1000 ? samplingRate / 1000 : samplingRate;
    }

    internal sealed record TrackLookupResult(TrackSnapshot Track, TrackLookupSource Source);

    internal enum TrackLookupSource
    {
        LTrackById,
        LTrackByTitleAndArtist,
        STrackByTitleAndArtist
    }
}
