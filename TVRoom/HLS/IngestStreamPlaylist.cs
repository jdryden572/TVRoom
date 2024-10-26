using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace TVRoom.HLS
{
    public record struct IngestSegmentReference(string FileName, double Duration);

    public sealed class IngestStreamPlaylist
    {
        public int HlsVersion { get; }
        public int TargetDuration { get; }
        public IReadOnlyList<IngestSegmentReference> SegmentReferences { get; }

        private IngestStreamPlaylist(int hlsVersion, int targetDuration, IReadOnlyList<IngestSegmentReference> segmentReferences)
        {
            HlsVersion = hlsVersion;
            TargetDuration = targetDuration;
            SegmentReferences = segmentReferences;
        }

        public static bool TryParse(ReadOnlySpan<byte> payload, [MaybeNullWhen(false)] out IngestStreamPlaylist parsed)
        {
            var version = int.MinValue;
            var targetDuration = int.MinValue;
            var segments = new List<IngestSegmentReference>();

            var buffer = ArrayPool<char>.Shared.Rent(payload.Length * 2);

            try
            {
                var length = Encoding.UTF8.GetChars(payload, buffer);
                var text = buffer.AsSpan().Slice(0, length);

                var currentSegmentDuration = double.MinValue;
                
                foreach (var line in MemoryExtensions.EnumerateLines(text))
                {
                    if (TryParseVersion(line, out var parsedVersion))
                    {
                        version = parsedVersion;
                        continue;
                    }

                    if (TryParseTargetDuration(line, out var parsedTargetDuration))
                    {
                        targetDuration = parsedTargetDuration;
                        continue;
                    }

                    if (currentSegmentDuration > double.MinValue && !line.StartsWith("#"))
                    {
                        // previous line was #EXTINF:
                        var segment = new IngestSegmentReference(line.ToString(), currentSegmentDuration);
                        segments.Add(segment);
                    }

                    if (TryParseSegmentDuration(line, out currentSegmentDuration))
                    {
                        continue;
                    }
                    else
                    {
                        currentSegmentDuration = double.MinValue;
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            if (version > int.MinValue && targetDuration > int.MinValue)
            {
                parsed = new IngestStreamPlaylist(version, targetDuration, segments.AsReadOnly());
                return true;
            }

            parsed = null;
            return false;
        }

        private static bool TryParseVersion(ReadOnlySpan<char> line, out int version)
        {
            const string prefix = "#EXT-X-VERSION:";
            if (line.StartsWith(prefix) && 
                int.TryParse(line.Slice(prefix.Length), CultureInfo.InvariantCulture, out version))
            {
                return true;
            }

            version = int.MinValue;
            return false;
        }

        private static bool TryParseTargetDuration(ReadOnlySpan<char> line, out int targetDuration)
        {
            const string prefix = "#EXT-X-TARGETDURATION:";
            if (line.StartsWith(prefix) &&
                int.TryParse(line.Slice(prefix.Length), CultureInfo.InvariantCulture, out targetDuration))
            {
                return true;
            }

            targetDuration = int.MinValue;
            return false;
        }

        private static bool TryParseSegmentDuration(ReadOnlySpan<char> line, out double duration)
        {
            const string prefix = "#EXTINF:";
            if (line.StartsWith(prefix) && line.EndsWith(","))
            {
                var slice = line.Slice(prefix.Length, line.Length - prefix.Length - 1);
                if (double.TryParse(slice, CultureInfo.InvariantCulture, out duration))
                {
                    return true;
                }
            }

            duration = double.MinValue;
            return false;
        }
    }
}
