using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using TVRoom.Configuration;
using TVRoom.HLS;
using TVRoom.Transcode;
using TVRoom.Tuner;

namespace TVRoom.Broadcast
{
    public sealed partial class BroadcastSession : IDisposable
    {
        private static readonly TimeSpan DebugOutputRetention = TimeSpan.FromSeconds(60);

        private readonly TranscodeSessionManager _sessionManager;
        private readonly BroadcastHistoryService _broadcastHistoryService;
        private readonly ILogger _logger;
        private readonly BehaviorSubject<TranscodeSession> _transcodeSessions;
        private readonly CompositeDisposable _cleanup;
        private readonly ScopedBufferPool _bufferPool;

        public BroadcastSession(
            BroadcastInfo broadcastInfo,
            TranscodeSession transcodeSession,
            TranscodeSessionManager sessionManager,
            TunerStatusProvider tunerStatusProvider,
            BroadcastHistoryService broadcastHistoryService,
            HlsConfiguration hlsConfig,
            ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(tunerStatusProvider);

            BroadcastInfo = broadcastInfo;
            _sessionManager = sessionManager;
            _logger = logger;
            _bufferPool = transcodeSession.BufferPool;
            _transcodeSessions = new(transcodeSession);
            HlsLiveStream = new(transcodeSession.FileIngester.StreamSegments, hlsConfig);

            var debugOutput = _transcodeSessions
                .Select(s => s.FFmpegOutput)
                .Switch()
                .Replay(DebugOutputRetention);

            debugOutput.WriteTranscodeLogsToFile(BroadcastInfo, hlsConfig);
            debugOutput.Connect();
            DebugOutput = debugOutput;

            // subscribe to tuner statuses, to ensure they are collected for the duration of the broadcast.
            // Don't actually need to use them here, but this ensures no discontinuities in the status history
            // while a stream is active.
            var unsubscribeTunerStatus = tunerStatusProvider.Statuses.Subscribe();
            _broadcastHistoryService = broadcastHistoryService;

            CancellationTokenSource autoStopCts = new(hlsConfig.MaxDuration);
            var unsubscribeToAutoStop = autoStopCts.Token.Register(() =>
            {
                LogBroadcastReachedMaxDuration(hlsConfig.MaxDuration);
                Task.Run(StopAsync);
            });

            _cleanup = new CompositeDisposable(
                unsubscribeTunerStatus, 
                unsubscribeToAutoStop, 
                autoStopCts,
                _transcodeSessions,
                _bufferPool);
        }

        public BroadcastInfo BroadcastInfo { get; }

        public MergedHlsLiveStream HlsLiveStream { get; }

        public IObservable<string> DebugOutput { get; }

        public IObservable<TranscodeStats> TranscodeStats => _transcodeSessions.Select(s => s.Stats).Switch();

        public Task Finished => _transcodeSessions.ToTask();

        public async Task StartAsync()
        {
            await _broadcastHistoryService.StartNewBroadcast(BroadcastInfo.ChannelInfo);
            TranscodeSession.Start();
        }

        public async Task StopAsync()
        {
            await _broadcastHistoryService.EndCurrentBroadcast();
            await TranscodeSession.StopAsync();
            _transcodeSessions.OnCompleted();
        }

        public async Task RestartTranscodeAsync()
        {
            await TranscodeSession.StopAsync();
            TranscodeSession.Dispose();

            var newTranscode = await _sessionManager.CreateTranscode(BroadcastInfo.ChannelInfo.Url, _logger, _bufferPool);
            HlsLiveStream.SetNewSource(newTranscode.FileIngester.StreamSegments);
            _transcodeSessions.OnNext(newTranscode);
            newTranscode.Start();
        }

        public void Dispose()
        {
            TranscodeSession.Dispose();
            HlsLiveStream.Dispose();
            _transcodeSessions.OnCompleted();
            _transcodeSessions.Dispose();
            _cleanup.Dispose();
        }

        private TranscodeSession TranscodeSession => _transcodeSessions.Value;

        [LoggerMessage(LogLevel.Warning, "Broadcast has reached is max duration of {MaxDuration}. Stopping it now.")]
        private partial void LogBroadcastReachedMaxDuration(TimeSpan maxDuration);
    }
}
