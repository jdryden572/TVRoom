using Serilog;

namespace TVRoom.Broadcast
{
    public sealed class TranscodeLogObserver : IObserver<string>, IDisposable
    {
        private readonly Serilog.Core.Logger _log;

        public TranscodeLogObserver(HlsConfiguration hlsConfig, BroadcastInfo broadcastInfo)
        {
            var timeString = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var logName = $"{timeString}_Channel{broadcastInfo.ChannelInfo.GuideNumber.Replace('.', '-')}_{broadcastInfo.SessionId}.log";
            var logPath = Path.Combine(hlsConfig.BaseTranscodeDirectory.FullName, logName);

            _log = new LoggerConfiguration()
                .WriteTo.File(logPath, flushToDiskInterval: TimeSpan.FromSeconds(30))
                .CreateLogger();
        }

        public void OnCompleted() => _log.Warning("Transcode output observable completed!");

        public void OnError(Exception error) => _log.Error(error, "Error from transcode output observable");

        public void OnNext(string value) => _log.Information(value);

        public void Dispose() => _log.Dispose();
    }
}
