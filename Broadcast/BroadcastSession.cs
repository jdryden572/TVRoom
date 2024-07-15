using System.Threading.Channels;

namespace LivingRoom.Broadcast
{
    public sealed class BroadcastSession : IDisposable
    {
        private static readonly BoundedChannelOptions _debugOutputChannelOptions = 
            new BoundedChannelOptions(200) 
            { 
                FullMode = BoundedChannelFullMode.DropOldest, 
                SingleReader = true, 
                SingleWriter = true,
            };

        private readonly FFmpegProcess _ffmpegProcess;
        private readonly HlsConfiguration _transcodeConfig;
        private readonly ILogger _logger;
        private readonly CancellationTokenRegistration _tokenRegistration;

        public BroadcastSession(BroadcastInfo broadcastInfo, DirectoryInfo transcodeDirectory, FFmpegProcess ffmpegProcess, HlsConfiguration transcodeConfig, ILogger logger)
        {
            BroadcastInfo = broadcastInfo;
            TranscodeDirectory = transcodeDirectory;
            _ffmpegProcess = ffmpegProcess;
            _transcodeConfig = transcodeConfig;
            _logger = logger;
            _tokenRegistration = _transcodeConfig.ApplicationStopping.Register(Dispose);
        }

        public BroadcastInfo BroadcastInfo { get; }

        public DirectoryInfo TranscodeDirectory { get; }

        public bool IsReady { get; private set; }

        public async Task StartAndWaitForReadyAsync()
        {
            var waitForTranscodeReady = new WaitForTranscodeReadyObserver(_transcodeConfig.HlsPlaylistReadyCount);
            using (_ffmpegProcess.Subscribe(waitForTranscodeReady))
            {
                _ffmpegProcess.Start();
                await waitForTranscodeReady.TranscodeReady;
                IsReady = true;
            }
        }

        public async Task StopAsync() => await _ffmpegProcess.StopAsync();

        public ChannelReader<string> GetDebugOutput(CancellationToken unsubscribe)
        {
            var channel = Channel.CreateBounded<string>(_debugOutputChannelOptions);
            var observer = new WriteToChannelObserver(channel.Writer);
            var unsubscriber = _ffmpegProcess.Subscribe(observer);

            unsubscribe.Register(() =>
            {
                unsubscriber.Dispose();
                observer.Dispose();
            });

            return channel.Reader;
        }

        public void Dispose()
        {
            _ffmpegProcess.Dispose();
            _tokenRegistration.Dispose();
            try
            {
                TranscodeDirectory.Delete(recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting transcode directory '{directory}'", TranscodeDirectory.FullName);
            }
        }
    }
}
