using CommunityToolkit.HighPerformance.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using TVRoom.HLS;

namespace TVRoom.Tests
{
    public static class FixedQueueExtensions
    {
        public static FixedQueue<T> ToFixedQueue<T>(this T[] array, int maxSize)
        {
            return FixedQueue<T>.Create(maxSize, array);
        }
    }

    [TestClass]
    public class HlsStreamTests
    {
        private readonly HlsStreamState _stream = new HlsStreamState(
                StreamInfo: "BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"",
                HlsVersion: 3,
                TargetDuration: 3,
                MediaSequence: 7,
                LiveEntries: new HlsPlaylistEntry[]
                {
                    new HlsSegmentPlaylistEntry(7, 1.5015, new HlsSegment("First.ts", GetPayload("FirstPayload"u8))),
                    new HlsSegmentPlaylistEntry(8, 3.003, new HlsSegment("Second.ts", GetPayload("SecondPayload"u8))),
                    new HlsDiscontinuityPlaylistEntry(),
                    new HlsSegmentPlaylistEntry(9, 3.003, new HlsSegment("Third.ts", GetPayload("ThidPayload"u8))),
                }.ToFixedQueue(10),
                PreviousSegments: new FixedQueue<HlsPlaylistEntry>(10));

        [TestMethod]
        public void Write_MasterPlaylist()
        {
            var writer = new ArrayBufferWriter<byte>();
            _stream.WriteMasterPlaylist(writer);

            var expected =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-STREAM-INF:BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS="avc1.4d002a,mp4a.40.2"
                live.m3u8
                """;
            var result = Encoding.UTF8.GetString(writer.WrittenSpan);
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void Write_StreamPlaylist()
        {
            var writer = new ArrayBufferWriter<byte>();
            _stream.WriteStreamPlaylist(writer);

            var expected =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-TARGETDURATION:3
                #EXT-X-MEDIA-SEQUENCE:7
                #EXTINF:1.501500,
                live7.ts
                #EXTINF:3.003000,
                live8.ts
                #EXT-X-DISCONTINUITY
                #EXTINF:3.003000,
                live9.ts
                """;
            var result = Encoding.UTF8.GetString(writer.WrittenSpan);
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void Write_StreamPlaylist_EnsureInvariant()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR", false);

                var writer = new ArrayBufferWriter<byte>();
                _stream.WriteStreamPlaylist(writer);

                var expected =
                    """
                    #EXTM3U
                    #EXT-X-VERSION:3
                    #EXT-X-TARGETDURATION:3
                    #EXT-X-MEDIA-SEQUENCE:7
                    #EXTINF:1.501500,
                    live7.ts
                    #EXTINF:3.003000,
                    live8.ts
                    #EXT-X-DISCONTINUITY
                    #EXTINF:3.003000,
                    live9.ts
                    """;
                var result = Encoding.UTF8.GetString(writer.WrittenSpan);
                Assert.AreEqual(expected, result);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        private static MemoryOwner<byte> GetPayload(ReadOnlySpan<byte> data)
        {
            var owner = MemoryOwner<byte>.Allocate(data.Length);
            data.CopyTo(owner.Span);
            return owner;
        }
    }
}