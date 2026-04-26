using System.Globalization;

namespace QobuzPresence.Models;

public sealed record AudioQuality(
    int BitDepth,
    double SamplingRateKhz,
    int? ChannelCount,
    bool IsHiRes,
    string Source)
{
    public string DisplayText
    {
        get
        {
            string tier = IsHiRes ? "Hi-Res" : "Lossless";
            string rate = SamplingRateKhz.ToString("0.###", CultureInfo.InvariantCulture);
            string channel = ChannelCount switch
            {
                2 => "Stereo",
                > 0 => $"{ChannelCount}ch",
                _ => string.Empty
            };

            string result = $"{tier} • {BitDepth}-bit / {rate} kHz";

            if (!string.IsNullOrWhiteSpace(channel))
            {
                result += $" • {channel}";
            }

            return result;
        }
    }
}
