﻿using TVRoom.HLS;

namespace TVRoom.Tests.HLS
{

    [TestClass]
    public class ParsedMasterPlaylistTests
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

            Assert.IsTrue(ParsedMasterPlaylist.TryParse(validMasterPlaylist, out var master));
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

            Assert.IsFalse(ParsedMasterPlaylist.TryParse(missingVersion, out _));
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

            Assert.IsFalse(ParsedMasterPlaylist.TryParse(missingStreamInfo, out _));
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

            Assert.IsTrue(ParsedMasterPlaylist.TryParse(multipleStreams, out var master));
            Assert.AreEqual(3, master.HlsVersion);
            Assert.AreEqual("BANDWIDTH=6740800,RESOLUTION=1280x720,CODECS=\"avc1.4d002a,mp4a.40.2\"", master.StreamInfo);
        }
    }
}