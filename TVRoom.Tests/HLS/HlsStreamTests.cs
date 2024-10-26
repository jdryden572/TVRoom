using CommunityToolkit.HighPerformance.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using TVRoom.HLS;

namespace TVRoom.Tests.HLS
{
    [TestClass]
    public class HlsStreamStateTests
    {
        private MemoryStream _memoryStream = new();

        private readonly HlsStreamWithSegments _streamState = new HlsStreamWithSegments(
            HlsListSize: 10,
            StreamInfo: "BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"",
            HlsVersion: 3,
            TargetDuration: 3,
            LiveSegments: HlsSegmentQueue.Create(3, new[]
            {
                new HlsSegmentEntry(7, 1.5015, GetPayload("FirstPayload"u8)),
                new HlsSegmentFollowedByDiscontinuity(8, 3.003, GetPayload("SecondPayload"u8)),
                new HlsSegmentEntry(9, 3.003, GetPayload("ThidPayload"u8)),
            }),
            PreviousSegments: new HlsSegmentQueue(3));

        [TestMethod]
        public void GetNext_FirstSegment()
        {
            var segmentInfo = new HlsSegmentInfo(_streamState.StreamInfo, 3, 4, 4.5, GetPayload("New segment!"u8));
            var notReady = new HlsStreamNotReady(5);
            var next = (HlsStreamWithSegments)notReady.WithNewSegment(segmentInfo);

            Assert.AreEqual(_streamState.StreamInfo, next.StreamInfo);
            Assert.AreEqual(5, next.HlsListSize);
            Assert.AreEqual(3, next.HlsVersion);
            Assert.AreEqual(4, next.TargetDuration);
            Assert.AreEqual(1, next.LiveSegments.Count);
            Assert.AreSame(segmentInfo.Payload, next.LiveSegments[0].Payload);
            Assert.AreEqual(0, next.PreviousSegments.Count);

        }

        [TestMethod]
        public void GetNext_NewSegment()
        {
            var segmentInfo = new HlsSegmentInfo(_streamState.StreamInfo, 3, 3, 4.5, GetPayload("New segment!"u8));
            var next = (HlsStreamWithSegments)_streamState.WithNewSegment(segmentInfo);
            Assert.AreEqual(8, next.MediaSequence);

            Assert.AreEqual(3, next.LiveSegments.Count);
            var newOne = next.LiveSegments[2];
            Assert.AreEqual(10, newOne.Index);
            Assert.AreEqual(4.5, newOne.Duration);
            Assert.AreSame(segmentInfo.Payload, newOne.Payload);

            Assert.AreSame(_streamState.LiveSegments[1], next.LiveSegments[0]);
            Assert.AreSame(_streamState.LiveSegments[2], next.LiveSegments[1]);

            Assert.AreSame(_streamState.LiveSegments[0], next.PreviousSegments[0]);
        }

        [TestMethod]
        public void GetNext_Discontinuity()
        {
            var next = (HlsStreamWithSegments)_streamState.WithNewDiscontinuity();
            Assert.AreEqual(3, next.LiveSegments.Count);

            Assert.AreSame(_streamState.LiveSegments[0], next.LiveSegments[0]);
            Assert.AreSame(_streamState.LiveSegments[1], next.LiveSegments[1]);

            var newOne = next.LiveSegments[2];
            Assert.IsInstanceOfType(newOne, typeof(HlsSegmentFollowedByDiscontinuity));
            Assert.AreEqual(_streamState.LiveSegments[2].Index, newOne.Index);
            Assert.AreEqual(_streamState.LiveSegments[2].Duration, newOne.Duration);
            Assert.AreSame(_streamState.LiveSegments[2].Payload, newOne.Payload);

            Assert.AreEqual(0, next.PreviousSegments.Count);
        }

        [TestMethod]
        public void GetNext_DisposesOldestPreviousSegment()
        {
            var next = _streamState;
            for (var i = 0; i < 4; i++)
            {
                var segmentInfo = new HlsSegmentInfo(_streamState.StreamInfo, 3, 3, 4.5, GetPayload("New segment!"u8));
                next = (HlsStreamWithSegments)next.WithNewSegment(segmentInfo);
            }

            Assert.IsTrue(_streamState.LiveSegments[0].Payload.IsBufferDisposed);
            Assert.IsFalse(_streamState.LiveSegments[1].Payload.IsBufferDisposed);
        }

        [TestMethod]
        public void DisposeAllSegments()
        {
            // Push one segment into Previous
            var segmentInfo = new HlsSegmentInfo(_streamState.StreamInfo, 3, 3, 4.5, GetPayload("New segment!"u8));
            var next = (HlsStreamWithSegments)_streamState.WithNewSegment(segmentInfo);

            next.DisposeAllSegments();

            foreach (var segment in next.LiveSegments)
            {
                Assert.IsTrue(segment.Payload.IsBufferDisposed);
            }

            Assert.IsTrue(next.PreviousSegments.Single().Payload.IsBufferDisposed);
        }

        [TestMethod]
        public async Task Write_MasterPlaylist()
        {
            var result = _streamState.GetMasterPlaylist();
            await result.ExecuteAsync(CreateMockContext());

            var expected =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-STREAM-INF:BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS="avc1.4d002a,mp4a.40.2"
                live.m3u8
                """;

            Assert.AreEqual(expected, GetWrittenUtf8String());
        }

        [TestMethod]
        public async Task Write_StreamPlaylist()
        {
            var result = _streamState.GetPlaylist();
            await result.ExecuteAsync(CreateMockContext());

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

            Assert.AreEqual(expected, GetWrittenUtf8String());
        }

        [TestMethod]
        public async Task Write_StreamPlaylist_EnsureInvariant()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR", false);

                var result = _streamState.GetPlaylist();
                await result.ExecuteAsync(CreateMockContext());

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

                Assert.AreEqual(expected, GetWrittenUtf8String());
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        private HttpContext CreateMockContext()
        {
            return new DefaultHttpContext
            {
                Response =
                {
                    Body = _memoryStream,
                }
            };
        }

        private string GetWrittenUtf8String()
        {
            var length = (int)_memoryStream.Length;
            var span = _memoryStream.GetBuffer().AsSpan().Slice(0, length);
            return Encoding.UTF8.GetString(span);
        }

        private static SharedBuffer GetPayload(ReadOnlySpan<byte> data)
        {
            return SharedBuffer.Create(new System.Buffers.ReadOnlySequence<byte>(data.ToArray()));
        }
    }
}