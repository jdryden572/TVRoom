using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace TVRoom.HLS
{
    public sealed class IngestMasterPlaylist
    {
        public int HlsVersion { get; }
        public string StreamInfo { get; }

        private IngestMasterPlaylist(int hlsVersion, string streamInfo)
        {
            HlsVersion = hlsVersion;
            StreamInfo = streamInfo;
        }

        public static bool TryParse(ReadOnlySpan<byte> payload, [MaybeNullWhen(false)] out IngestMasterPlaylist parsed)
        {
            var version = int.MinValue;
            var streamInfo = string.Empty;

            var buffer = ArrayPool<char>.Shared.Rent(payload.Length * 2);

            try
            {
                var length = Encoding.UTF8.GetChars(payload, buffer);
                var text = buffer.AsSpan().Slice(0, length);
                foreach (var line in MemoryExtensions.EnumerateLines(text))
                {
                    const string versionPrefix = "#EXT-X-VERSION:";
                    if (line.StartsWith(versionPrefix) &&
                        int.TryParse(line.Slice(versionPrefix.Length), CultureInfo.InvariantCulture, out var parsedVersion))
                    {
                        version = parsedVersion;
                        continue;
                    }

                    const string streamInfoPrefix = "#EXT-X-STREAM-INF:";
                    if (line.StartsWith(streamInfoPrefix))
                    {
                        streamInfo = line.Slice(streamInfoPrefix.Length).ToString();
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            if (version > int.MinValue && !string.IsNullOrEmpty(streamInfo))
            {
                parsed = new IngestMasterPlaylist(version, streamInfo);
                return true;
            }

            parsed = null;
            return false;
        }
    }
}
