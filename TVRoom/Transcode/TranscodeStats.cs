using System.Globalization;
using System.Text.Json.Serialization;

namespace TVRoom.Transcode
{
    public readonly record struct TranscodeStats(
        [property: JsonPropertyName("fps")]float FramesPerSecond,
        [property: JsonPropertyName("q")]float Quality,
        [property: JsonPropertyName("s")]float Speed,
        [property: JsonPropertyName("dup")]int Duplicate,
        [property: JsonPropertyName("drop")] int Dropped)
    {
        public static bool TryParse(ReadOnlySpan<char> line, out TranscodeStats stats)
        {
            const string fpsPrefix = "fps=";
            const string qualityPrefix = "q=";
            const string sizePrefix = "size=";
            const string speedPrefix = "speed=";
            const string dupPrefix = "dup=";
            const string dropPrefix = "drop=";

            var fpsStart = line.IndexOf(fpsPrefix);
            var qualityStart = line.IndexOf(qualityPrefix);
            var sizeStart = line.IndexOf(sizePrefix);
            var speedStart = line.IndexOf(speedPrefix);
            if (fpsStart >= 0 && qualityStart >= 0 && sizeStart >= 0 && speedStart >= 0)
            {
                var fpsSlice = line.Slice(fpsStart + fpsPrefix.Length, qualityStart - fpsStart - fpsPrefix.Length);
                var qualitySlice = line.Slice(qualityStart + qualityPrefix.Length, sizeStart - qualityStart - qualityPrefix.Length);
                var speedSlice = line.Slice(speedStart + speedPrefix.Length);
                speedSlice = speedSlice.Slice(0, speedSlice.IndexOf('x'));
                
                if (float.TryParse(fpsSlice, CultureInfo.InvariantCulture, out var fps) &&
                    float.TryParse(qualitySlice, CultureInfo.InvariantCulture, out var quality) &&
                    float.TryParse(speedSlice, CultureInfo.InvariantCulture, out var speed))
                {
                    // We have all the values that are required, the next ones are optional...
                    int duplicate = 0;
                    int dropped = 0;

                    var dupStart = line.IndexOf(dupPrefix);
                    if (dupStart >= 0)
                    {
                        var dupSlice = line.Slice(dupStart + dupPrefix.Length);
                        dupSlice = dupSlice.Slice(0, dupSlice.IndexOf(' '));
                        int.TryParse(dupSlice, CultureInfo.InvariantCulture, out duplicate);
                    }

                    var dropStart = line.IndexOf(dropPrefix);
                    if (dropStart >= 0)
                    {
                        var dropSlice = line.Slice(dropStart + dropPrefix.Length);
                        dropSlice = dropSlice.Slice(0, dropSlice.IndexOf(' '));
                        int.TryParse(dropSlice, CultureInfo.InvariantCulture, out dropped);
                    }

                    stats = new TranscodeStats(fps, quality, speed, duplicate, dropped);
                    return true;
                }
            }

            stats = default;
            return false;
        }
    }
}
