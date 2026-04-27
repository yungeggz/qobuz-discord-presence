using System.Text.Json;
using QobuzPresence.Models;

namespace QobuzPresence.Helpers;

internal static class QobuzTrackMetadataParser
{
    public static ParsedTrackMetadata Parse(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return ParsedTrackMetadata.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(dataJson);
            JsonElement root = document.RootElement;

            string? title = JsonElementHelper.GetString(root, "title");
            string? artist = JsonElementHelper.GetNestedString(root, "performer", "name")
                ?? JsonElementHelper.GetNestedString(root, "artist", "name")
                ?? JsonElementHelper.GetNestedString(root, "album", "artist", "name");
            string? albumTitle = JsonElementHelper.GetNestedString(root, "album", "title");

            TimeSpan? duration = JsonElementHelper.GetDouble(root, "duration") is double durationSeconds
                ? TimeSpan.FromSeconds(durationSeconds)
                : null;

            AudioQuality? quality = TryReadQuality(root, "Track metadata")
                ?? TryReadQualityFromChild(root, "album", "Album metadata");

            string? coverImageUrl = JsonElementHelper.GetNestedString(root, "album", "assetsAPI", "large")
                ?? JsonElementHelper.GetNestedString(root, "album", "assetsAPI", "small")
                ?? JsonElementHelper.GetNestedString(root, "album", "image", "large")
                ?? JsonElementHelper.GetNestedString(root, "album", "image", "small");

            if (coverImageUrl is not null && coverImageUrl.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
            {
                coverImageUrl = null;
            }

            return new ParsedTrackMetadata(title, artist, albumTitle, quality, coverImageUrl, duration);
        }
        catch
        {
            return ParsedTrackMetadata.Empty;
        }
    }

    private static AudioQuality? TryReadQualityFromChild(JsonElement root, string childName, string source)
    {
        if (!JsonElementHelper.TryGetProperty(root, childName, out JsonElement child))
        {
            return null;
        }

        return TryReadQuality(child, source);
    }

    private static AudioQuality? TryReadQuality(JsonElement element, string source)
    {
        int? bitDepth = JsonElementHelper.GetInt32(element, "maximum_bit_depth") ?? JsonElementHelper.GetInt32(element, "bit_depth");
        double? samplingRate = JsonElementHelper.GetDouble(element, "maximum_sampling_rate") ?? JsonElementHelper.GetDouble(element, "sampling_rate");
        int? channelCount = JsonElementHelper.GetInt32(element, "maximum_channel_count") ?? JsonElementHelper.GetInt32(element, "channel_count");
        bool hires = JsonElementHelper.GetBool(element, "hires") == true || JsonElementHelper.GetBool(element, "hires_streamable") == true;

        if (bitDepth is null || samplingRate is null)
        {
            return null;
        }

        double samplingRateKhz = samplingRate.Value >= 1000 ? samplingRate.Value / 1000 : samplingRate.Value;
        bool isHiRes = hires || bitDepth.Value > 16 || samplingRateKhz > 44.1;

        return new AudioQuality(bitDepth.Value, samplingRateKhz, channelCount, isHiRes, source);
    }
}

internal sealed record ParsedTrackMetadata(
    string? Title,
    string? Artist,
    string? AlbumTitle,
    AudioQuality? Quality,
    string? CoverImageUrl,
    TimeSpan? Duration)
{
    public static ParsedTrackMetadata Empty { get; } = new(null, null, null, null, null, null);
}
