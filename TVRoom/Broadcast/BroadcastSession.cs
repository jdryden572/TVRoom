using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TVRoom.HLS;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastSession : IDisposable
    {
        private readonly HlsTranscode _hlsTranscode;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILogger _logger;
        private readonly CancellationTokenRegistration _tokenRegistration;

        public BroadcastSession(BroadcastInfo broadcastInfo, DirectoryInfo transcodeDirectory, HlsTranscode hlsTranscode, HlsConfiguration hlsConfig, ILogger logger)
        {
            BroadcastInfo = broadcastInfo;
            TranscodeDirectory = transcodeDirectory;
            _hlsTranscode = hlsTranscode;
            _hlsConfig = hlsConfig;
            _logger = logger;
            _tokenRegistration = _hlsConfig.ApplicationStopping.Register(Dispose);

            _hlsTranscode.FFmpegProcess.FFmpegOutput.WriteTranscodeLogsToFile(broadcastInfo, hlsConfig);
        }

        public BroadcastInfo BroadcastInfo { get; }

        public DirectoryInfo TranscodeDirectory { get; }

        public HlsLiveStream HlsLiveStream => _hlsTranscode.HlsLiveStream;

        public bool IsReady { get; private set; }

        public async Task StartAndWaitForReadyAsync()
        {
            var waitForReady = _hlsTranscode.FFmpegProcess.FFmpegOutput
                .Where(line => HlsRegexPatterns.WritingHlsSegment().IsMatch(line))
                .Take(_hlsConfig.HlsPlaylistReadyCount)
                .Count();

            _hlsTranscode.Start();
            await waitForReady;
            IsReady = true;
        }

        public async Task StopAsync() => await _hlsTranscode.StopAsync();

        public ChannelReader<string> GetDebugOutput(CancellationToken unsubscribe) => _hlsTranscode.FFmpegProcess.FFmpegOutput.AsChannelReader(unsubscribe);

        public void Dispose()
        {
            _hlsTranscode.Dispose();
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

    internal static partial class HlsRegexPatterns
    {
        [GeneratedRegex(@"^\[hls @ \S+\] Opening '.*?\.ts'")]
        public static partial Regex WritingHlsSegment();
    }
}
