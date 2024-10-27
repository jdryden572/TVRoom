using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;
using TVRoom.Configuration;
using TVRoom.HLS;
using TVRoom.Transcode;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastSession : IDisposable
    {
        private readonly TranscodeSessionManager _sessionManager;
        private readonly ILogger _logger;
        private readonly BehaviorSubject<TranscodeSession> _transcodeSessions;
        private readonly ReplaySubject<string> _debugOutput;

        public BroadcastSession(BroadcastInfo broadcastInfo, TranscodeSession transcodeSession, TranscodeSessionManager sessionManager, HlsConfiguration hlsConfig, ILogger logger)
        {
            BroadcastInfo = broadcastInfo;
            _sessionManager = sessionManager;
            _logger = logger;
            _transcodeSessions = new(transcodeSession);
            HlsLiveStream = new(transcodeSession.FileIngester.StreamSegments, hlsConfig);
            _debugOutput = new(50);
            _transcodeSessions
                .Select(s => s.FFmpegProcess.FFmpegOutput)
                .Switch()
                .Subscribe(_debugOutput);

            _debugOutput.WriteTranscodeLogsToFile(BroadcastInfo, hlsConfig);
        }

        public BroadcastInfo BroadcastInfo { get; }

        public MergedHlsLiveStream HlsLiveStream { get; }

        public TranscodeSession TranscodeSession => _transcodeSessions.Value;

        public void Start() => TranscodeSession.Start();

        public async Task StopAsync() => await TranscodeSession.StopAsync();

        public async Task RestartTranscodeAsync()
        {
            await TranscodeSession.StopAsync();
            TranscodeSession.Dispose();

            var newTranscode = await _sessionManager.CreateTranscode(BroadcastInfo.ChannelInfo.Url, _logger);
            HlsLiveStream.SetNewSource(newTranscode.FileIngester.StreamSegments);
            _transcodeSessions.OnNext(newTranscode);
            newTranscode.Start();
        }

        public ChannelReader<string> GetDebugOutput(CancellationToken unsubscribe) => _debugOutput.AsChannelReader(unsubscribe);

        public void Dispose()
        {
            TranscodeSession.Dispose();
            HlsLiveStream.Dispose();
            _debugOutput.OnCompleted();
        }
    }
}
