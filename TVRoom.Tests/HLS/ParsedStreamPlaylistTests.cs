using Microsoft.VisualStudio.TestTools.UnitTesting;
using TVRoom.HLS;

namespace TVRoom.Tests.HLS
{
    [TestClass]
    public class ParsedStreamPlaylistTests
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

            Assert.IsTrue(ParsedStreamPlaylist.TryParse(validPlaylistFile, out var playlist));
            Assert.AreEqual(3, playlist.HlsVersion);
            Assert.AreEqual(3, playlist.TargetDuration);
            var expected = new[]
            {
                new IngestSegmentReference("live0.ts", 1.5015),
                new IngestSegmentReference("live1.ts", 3.003),
                new IngestSegmentReference("live2.ts", 3.003),
                new IngestSegmentReference("live3.ts", 3.003),
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

            Assert.IsFalse(ParsedStreamPlaylist.TryParse(noVersion, out _));
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

            Assert.IsFalse(ParsedStreamPlaylist.TryParse(noTargetDuration, out _));
        }

        [TestMethod]
        public void TryParse_InvalidInput_NoSegments()
        {
            var noTargetDuration =
                """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-TARGETDURATION:3
                #EXT-X-MEDIA-SEQUENCE:0
                """u8.ToArray();

            Assert.IsFalse(ParsedStreamPlaylist.TryParse(noTargetDuration, out _));
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

            Assert.IsTrue(ParsedStreamPlaylist.TryParse(someInvalidSegments, out var playlist));
            Assert.AreEqual(3, playlist.HlsVersion);
            Assert.AreEqual(3, playlist.TargetDuration);
            var expected = new[]
            {
                new IngestSegmentReference("live0.ts", 1.5015),
                new IngestSegmentReference("live3.ts", 3.003),
            };

            CollectionAssert.AreEqual(expected, playlist.SegmentReferences.ToArray());
        }
    }
}