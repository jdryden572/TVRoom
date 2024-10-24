using Serilog;
using System.Reactive.Linq;
using TVRoom.HLS;

namespace TVRoom.Broadcast
{
    internal static class TranscodeLogExtensions
    {
        public static void WriteTranscodeLogsToFile(this IObservable<string> transcodeOutput, BroadcastInfo broadcastInfo, HlsConfiguration hlsConfig)
        {
            var timeString = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var logName = $"{timeString}_Channel{broadcastInfo.ChannelInfo.GuideNumber.Replace('.', '-')}_{broadcastInfo.SessionId}.log";
            var logPath = Path.Combine(hlsConfig.BaseTranscodeDirectory.FullName, logName);
            var log = new LoggerConfiguration()
                .WriteTo.File(logPath, flushToDiskInterval: TimeSpan.FromSeconds(30))
                .CreateLogger();

            transcodeOutput
                .Finally(log.Dispose)
                .Subscribe(
                    log.Information,
                    e => log.Error(e, "Error from transcode output"),
                    () => log.Warning("Transcode output completed!"));
        }
    }
}
