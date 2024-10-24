using Microsoft.VisualStudio.TestTools.UnitTesting;
using TVRoom.HLS;

namespace TVRoom.Tests
{
    [TestClass]
    public class HlsMasterPlaylistTests
    {
        [TestMethod]
        public void TryParse_ValidInput()
        {
            var validMasterPlaylist =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-STREAM-INF:BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS="avc1.4d002a,mp4a.40.2"
                live.m3u8
                """u8.ToArray();

            Assert.IsTrue(HlsMasterPlaylist.TryParse(validMasterPlaylist, out var master));
            Assert.AreEqual(3, master.HlsVersion);
            Assert.AreEqual("BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"", master.StreamInfo);
        }

        [TestMethod]
        public void TryParse_InvalidInput_MissingVersion()
        {
            var missingVersion =
                """
                #EXTM3U
                #EXT-X-STREAM-INF:BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS="avc1.4d002a,mp4a.40.2"
                live.m3u8
                """u8.ToArray();

            Assert.IsFalse(HlsMasterPlaylist.TryParse(missingVersion, out _));
        }

        [TestMethod]
        public void TryParse_InvalidInput_MissingStreamInfo()
        {
            var missingStreamInfo =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                live.m3u8
                """u8.ToArray();

            Assert.IsFalse(HlsMasterPlaylist.TryParse(missingStreamInfo, out _));
        }

        [TestMethod]
        public void TryParse_AmbiguousInput_LastStreamInfoWins()
        {
            var multipleStreams =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-STREAM-INF:BANDWIDTH=50000,RESOLUTION=1280x720,CODECS="avc1.4d002a,mp4a.40.2"
                live.m3u8
                #EXT-X-STREAM-INF:BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS="avc1.4d002a,mp4a.40.2"
                live2.m3u8
                """u8.ToArray();

            Assert.IsTrue(HlsMasterPlaylist.TryParse(multipleStreams, out var master));
            Assert.AreEqual(3, master.HlsVersion);
            Assert.AreEqual("BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"", master.StreamInfo);
        }
    }

    [TestClass]
    public class HlsStreamPlaylistTests
    {
        [TestMethod]
        public void TryParse_ValidInput()
        {
            var validPlaylistFile =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-TARGETDURATION:3
                #EXT-X-MEDIA-SEQUENCE:0
                #EXTINF:1.501500,
                live0.ts
                #EXTINF:3.003000,
                live1.ts
                #EXTINF:3.003000,
                live2.ts
                #EXTINF:3.003000,
                live3.ts
                """u8.ToArray();

            Assert.IsTrue(HlsStreamPlaylist.TryParse(validPlaylistFile, out var playlist));
            Assert.AreEqual(3, playlist.HlsVersion);
            Assert.AreEqual(3, playlist.TargetDuration);
            var expected = new[]
            {
                new HlsSegmentReference("live0.ts", 1.5015),
                new HlsSegmentReference("live1.ts", 3.003),
                new HlsSegmentReference("live2.ts", 3.003),
                new HlsSegmentReference("live3.ts", 3.003),
            };

            CollectionAssert.AreEqual(expected, playlist.SegmentReferences.ToArray());
        }

        [TestMethod]
        public void TryParse_InvalidInput_NoVersion()
        {
            var noVersion =
                """
                #EXTM3U
                #EXT-X-TARGETDURATION:3
                #EXT-X-MEDIA-SEQUENCE:0
                #EXTINF:1.501500,
                live0.ts
                #EXTINF:3.003000,
                live1.ts
                """u8.ToArray();

            Assert.IsFalse(HlsStreamPlaylist.TryParse(noVersion, out _));
        }

        [TestMethod]
        public void TryParse_InvalidInput_NoTargetDuration()
        {
            var noTargetDuration =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-MEDIA-SEQUENCE:0
                #EXTINF:1.501500,
                live0.ts
                #EXTINF:3.003000,
                live1.ts
                """u8.ToArray();

            Assert.IsFalse(HlsStreamPlaylist.TryParse(noTargetDuration, out _));
        }

        [TestMethod]
        public void TryParse_SkipSegments_WithInvalidDuration()
        {
            var someInvalidSegments =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-TARGETDURATION:3
                #EXT-X-MEDIA-SEQUENCE:0
                #EXTINF:1.501500,
                live0.ts
                #EXTINF:3.003000
                live1.ts
                #EXTINF:3.003000,
                #EXTINF:3.003000,
                live3.ts
                """u8.ToArray();

            Assert.IsTrue(HlsStreamPlaylist.TryParse(someInvalidSegments, out var playlist));
            Assert.AreEqual(3, playlist.HlsVersion);
            Assert.AreEqual(3, playlist.TargetDuration);
            var expected = new[]
            {
                new HlsSegmentReference("live0.ts", 1.5015),
                new HlsSegmentReference("live3.ts", 3.003),
            };

            CollectionAssert.AreEqual(expected, playlist.SegmentReferences.ToArray());
        }
    }
}