using CommunityToolkit.HighPerformance.Buffers;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Reactive.Linq;
using TVRoom.HLS;

namespace TVRoom.Tests.HLS
{
    [TestClass]
    public class HlsFileIngesterTests
    {
        private static byte[] _validMasterPlaylist =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-STREAM-INF:BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS="avc1.4d002a,mp4a.40.2"
                live.m3u8
                """u8.ToArray();

        private ScopedBufferPool _scopedBufferPool = null!;

        private HlsFileIngester _fileIngester = null!;
        
        [TestInitialize]
        public void TestInitialize()
        {
            _scopedBufferPool = new ScopedBufferPool();
            _fileIngester = new HlsFileIngester(_scopedBufferPool);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _scopedBufferPool?.Dispose();
            _fileIngester?.Dispose();
        }

        [TestMethod]
        public async Task ExpectedSequence()
        {
            var segments = new List<HlsSegmentInfo>();
            _fileIngester.StreamSegments.Subscribe(segments.Add);

            var master = new IngestMasterPlaylist(GetPayload(_validMasterPlaylist));
            await _fileIngester.IngestStreamFileAsync(master);

            var firstPlaylist = new IngestStreamPlaylist(GetPayload(
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-TARGETDURATION:3
                #EXT-X-MEDIA-SEQUENCE:0
                #EXTINF:1.501500,
                live0.ts
                """u8));
            await _fileIngester.IngestStreamFileAsync(firstPlaylist);

            var firstSegment = new IngestStreamSegment("live0.ts", GetPayload("SomePayload!"u8));
            await _fileIngester.IngestStreamFileAsync(firstSegment);
            var segment = await _fileIngester.StreamSegments
                .Timeout(TimeSpan.FromSeconds(1))
                .FirstAsync();

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual("BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"", segment.StreamInfo);
            Assert.AreEqual(3, segment.HlsVersion);
            Assert.AreEqual(3, segment.TargetDuration);
            Assert.AreEqual(1.5015, segment.Duration);
            Assert.AreSame(firstSegment.Payload, segment.Payload);
            Assert.IsFalse(firstSegment.Payload.IsBufferDisposed);

            Assert.IsTrue(master.Payload.IsBufferDisposed);
            Assert.IsTrue(firstPlaylist.Payload.IsBufferDisposed);
        }

        [TestMethod]
        public async Task SkippedSegmentsDisposed()
        {
            var segments = new List<HlsSegmentInfo>();
            _fileIngester.StreamSegments.Subscribe(segments.Add);

            var master = new IngestMasterPlaylist(GetPayload(_validMasterPlaylist));
            await _fileIngester.IngestStreamFileAsync(master);

            // wrong name
            var firstSegment = new IngestStreamSegment("live_oops.ts", GetPayload("To be disposed...!"u8));
            await _fileIngester.IngestStreamFileAsync(firstSegment);

            var secondSegment = new IngestStreamSegment("live0.ts", GetPayload("SomePayload!"u8));
            await _fileIngester.IngestStreamFileAsync(secondSegment);

            var firstPlaylist = new IngestStreamPlaylist(GetPayload(
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-TARGETDURATION:3
                #EXT-X-MEDIA-SEQUENCE:0
                #EXTINF:1.501500,
                live0.ts
                """u8));
            await _fileIngester.IngestStreamFileAsync(firstPlaylist);

            var segment = await _fileIngester.StreamSegments
                .Timeout(TimeSpan.FromSeconds(1))
                .FirstAsync();

            Assert.IsTrue(firstSegment.Payload.IsBufferDisposed);
            Assert.IsFalse(secondSegment.Payload.IsBufferDisposed);

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual("BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"", segment.StreamInfo);
            Assert.AreEqual(3, segment.HlsVersion);
            Assert.AreEqual(3, segment.TargetDuration);
            Assert.AreEqual(1.5015, segment.Duration);
            Assert.AreSame(secondSegment.Payload, segment.Payload);
            Assert.IsFalse(segment.Payload.IsBufferDisposed);
        }

        [TestMethod]
        public async Task CompletesWhenDisposed()
        {
            var completionTask = _fileIngester.StreamSegments.LastOrDefaultAsync();
            _fileIngester.Dispose();
            Assert.IsNull(await completionTask);
        }

        private SharedBuffer GetPayload(ReadOnlySpan<byte> data)
        {
            var logger = new LoggerFactory().CreateLogger<SharedBuffer>();
            return SharedBuffer.Create(new ReadOnlySequence<byte>(data.ToArray()), logger, _scopedBufferPool);
        }
    }

    internal static class MemoryOwnerExtensions
    {
        public static bool IsDisposed(this MemoryOwner<byte> owner)
        {
            try
            {
                _ = owner.Span;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }

            return false;
        }
    }
}