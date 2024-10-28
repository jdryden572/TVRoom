using TVRoom.Transcode;

namespace TVRoom.Tests.Transcode
{
    [TestClass]
    public class TranscodeStatsTests
    {
        [TestMethod]
        [DataRow(
            "frame=115880 fps= 60 q=26.0 size=N/A time=00:32:09.19 bitrate=N/A speed= 1x",
            60f, 26.0f, 1.0f, 0, 0)]
        [DataRow(
            "frame= 1441 fps= 63 q=28.0 size=N/A time=00:00:22.40 bitrate=N/A speed=0.974x",
            63f, 28.0f, 0.974f, 0, 0)]
        [DataRow("frame= 564 fps= 61 q=27.0 size=N/A time=00:00:09.32 bitrate=N/A dup=83 drop=0 speed=1.01x",
            61f, 27.0f, 1.01f, 83, 0)]
        [DataRow("frame= 49 fps=9.8 q=25.0 size=N/A time=00:00:04.53 bitrate=N/A speed=0.906x",
            9.8f, 25.0f, 0.906f, 0, 0)]
        [DataRow("frame= 1450 fps= 63 q=10.0 size=N/A time=00:00:22.20 bitrate=N/A dup=139 drop=5 speed=0.965x",
            63f, 10f, 0.965f, 139, 5)]
        public void TryParse_Success(string line, float fps, float quality, float speed, int duplicate, int dropped)
        {
            Assert.IsTrue(TranscodeStats.TryParse(line, out var stats));
            Assert.AreEqual(fps, stats.FramesPerSecond);
            Assert.AreEqual(quality, stats.Quality);
            Assert.AreEqual(speed, stats.Speed);
            Assert.AreEqual(duplicate, stats.Duplicate);
            Assert.AreEqual(dropped, stats.Dropped);
        }
    }
}
