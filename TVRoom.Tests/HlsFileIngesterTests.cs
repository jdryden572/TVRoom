using CommunityToolkit.HighPerformance.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reactive.Linq;
using TVRoom.HLS;

namespace TVRoom.Tests
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

        private HlsFileIngester? _fileIngester = new();

        public void TestCleanup() => _fileIngester?.Dispose();

        [TestMethod]
        public async Task ExpectedSequence()
        {
            var segments = new List<HlsSegmentInfo>();
            _fileIngester!.StreamSegments.Subscribe(segments.Add);

            var master = new IngestHlsFile("master.m3u8", IngestFileType.MasterPlaylist, GetPayload(_validMasterPlaylist));
            await _fileIngester.IngestStreamFileAsync(master);

            var firstPlaylist = new IngestHlsFile("live.m3u8", IngestFileType.Playlist, GetPayload(
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-TARGETDURATION:3
                #EXT-X-MEDIA-SEQUENCE:0
                #EXTINF:1.501500,
                live0.ts
                """u8));
            await _fileIngester.IngestStreamFileAsync(firstPlaylist);

            var firstSegment = new IngestHlsFile("live0.ts", IngestFileType.Segment, GetPayload("SomePayload!"u8));
            await _fileIngester.IngestStreamFileAsync(firstSegment);
            var segment = await _fileIngester.StreamSegments
                .Timeout(TimeSpan.FromSeconds(1))
                .FirstAsync();

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual("BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"", segment.StreamInfo);
            Assert.AreEqual(3, segment.HlsVersion);
            Assert.AreEqual(3, segment.TargetDuration);
            Assert.AreEqual(1.5015, segment.Duration);
            Assert.AreEqual(firstSegment.FileName, segment.Segment.FileName);
            Assert.AreEqual(firstSegment.Payload.Length, segment.Segment.Length);
            Assert.IsFalse(firstSegment.Payload.IsDisposed());

            Assert.IsTrue(master.Payload.IsDisposed());
            Assert.IsTrue(firstPlaylist.Payload.IsDisposed());
        }

        [TestMethod]
        public async Task SkippedSegmentsDisposed()
        {
            var segments = new List<HlsSegmentInfo>();
            _fileIngester!.StreamSegments.Subscribe(segments.Add);

            var master = new IngestHlsFile("master.m3u8", IngestFileType.MasterPlaylist, GetPayload(_validMasterPlaylist));
            await _fileIngester.IngestStreamFileAsync(master);

            // wrong name
            var firstSegment = new IngestHlsFile("live_oops.ts", IngestFileType.Segment, GetPayload("To be disposed...!"u8));
            await _fileIngester.IngestStreamFileAsync(firstSegment);

            var secondSegment = new IngestHlsFile("live0.ts", IngestFileType.Segment, GetPayload("SomePayload!"u8));
            await _fileIngester.IngestStreamFileAsync(secondSegment);

            var firstPlaylist = new IngestHlsFile("live.m3u8", IngestFileType.Playlist, GetPayload(
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

            Assert.IsTrue(firstSegment.Payload.IsDisposed());
            Assert.IsFalse(secondSegment.Payload.IsDisposed());

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual("BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"", segment.StreamInfo);
            Assert.AreEqual(3, segment.HlsVersion);
            Assert.AreEqual(3, segment.TargetDuration);
            Assert.AreEqual(1.5015, segment.Duration);
            Assert.AreEqual(secondSegment.FileName, segment.Segment.FileName);
            Assert.AreEqual(secondSegment.Payload.Length, segment.Segment.Length);
        }

        [TestMethod]
        public async Task CompletesWhenDisposed()
        {
            var completionTask = _fileIngester!.StreamSegments.LastOrDefaultAsync();
            _fileIngester.Dispose();
            Assert.IsNull(await completionTask);
        }

        [TestMethod]
        public async Task DisposesUnusedSegmentsWhenDisposed()
        {
            var firstSegment = new IngestHlsFile("live_oops.ts", IngestFileType.Segment, GetPayload("To be disposed...!"u8));
            await _fileIngester!.IngestStreamFileAsync(firstSegment);

            var secondSegment = new IngestHlsFile("live0.ts", IngestFileType.Segment, GetPayload("To be disposed...!"u8));
            await _fileIngester!.IngestStreamFileAsync(secondSegment);

            var completion = _fileIngester.StreamSegments.LastOrDefaultAsync();
            _fileIngester!.Dispose();
            await completion;

            Assert.IsTrue(firstSegment.Payload.IsDisposed());
            Assert.IsTrue(secondSegment.Payload.IsDisposed());
        }

        private MemoryOwner<byte> GetPayload(ReadOnlySpan<byte> data)
        {
            var owner = MemoryOwner<byte>.Allocate(data.Length);
            data.CopyTo(owner.Span);
            return owner;
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