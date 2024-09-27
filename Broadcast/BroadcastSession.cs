using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastSession : IDisposable
    {
        private readonly FFmpegProcess _ffmpegProcess;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILogger _logger;
        private readonly CancellationTokenRegistration _tokenRegistration;

        public BroadcastSession(BroadcastInfo broadcastInfo, DirectoryInfo transcodeDirectory, FFmpegProcess ffmpegProcess, HlsConfiguration hlsConfig, ILogger logger)
        {
            BroadcastInfo = broadcastInfo;
            TranscodeDirectory = transcodeDirectory;
            HlsLiveStream = new HlsLiveStream(hlsConfig);
            _ffmpegProcess = ffmpegProcess;
            _hlsConfig = hlsConfig;
            _logger = logger;
            _tokenRegistration = _hlsConfig.ApplicationStopping.Register(Dispose);

            _ffmpegProcess.FFmpegOutput.WriteTranscodeLogsToFile(broadcastInfo, hlsConfig);
        }

        public BroadcastInfo BroadcastInfo { get; }

        public DirectoryInfo TranscodeDirectory { get; }

        public HlsLiveStream HlsLiveStream { get; }

        public bool IsReady { get; private set; }

        public async Task StartAndWaitForReadyAsync()
        {
            var waitForReady = _ffmpegProcess.FFmpegOutput
                .Where(line => HlsRegexPatterns.WritingHlsSegment().IsMatch(line))
                .Take(_hlsConfig.HlsPlaylistReadyCount)
                .Count();

            _ffmpegProcess.Start();
            await waitForReady;
            IsReady = true;
        }

        public async Task StopAsync() => await _ffmpegProcess.StopAsync();

        public ChannelReader<string> GetDebugOutput(CancellationToken unsubscribe) => _ffmpegProcess.FFmpegOutput.AsChannelReader(unsubscribe);

        public void Dispose()
        {
            _ffmpegProcess.Dispose();
            _tokenRegistration.Dispose();
            HlsLiveStream.Dispose();
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

    internal static partial class HlsRegexPatterns
    {
        [GeneratedRegex(@"^\[hls @ \S+\] Opening '.*?\.ts'")]
        public static partial Regex WritingHlsSegment();
    }
}
