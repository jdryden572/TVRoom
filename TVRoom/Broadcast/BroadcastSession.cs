using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TVRoom.Transcode;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastSession : IDisposable
    {
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILogger _logger;
        private readonly CancellationTokenRegistration _tokenRegistration;

        public BroadcastSession(BroadcastInfo broadcastInfo, HlsLiveStream liveStream, HlsConfiguration hlsConfig, ILogger logger)
        {
            BroadcastInfo = broadcastInfo;
            HlsLiveStream = liveStream;
            _hlsConfig = hlsConfig;
            _logger = logger;
            _tokenRegistration = _hlsConfig.ApplicationStopping.Register(Dispose);
        }

        public BroadcastInfo BroadcastInfo { get; }

        public HlsLiveStream HlsLiveStream { get; }

        public bool IsReady { get; private set; }

        public async Task StartAndWaitForReadyAsync()
        {
            var waitForReady = HlsLiveStream.DebugOutput
                .Where(line => HlsRegexPatterns.WritingHlsSegment().IsMatch(line))
                .Take(_hlsConfig.HlsPlaylistReadyCount)
                .Count();

            await HlsLiveStream.StartAsync();
            await waitForReady;
            IsReady = true;
        }

        public ChannelReader<string> GetDebugOutput(CancellationToken unsubscribe) => HlsLiveStream.DebugOutput.AsChannelReader(unsubscribe);

        public void Dispose()
        {
            HlsLiveStream.Dispose();
            _tokenRegistration.Dispose();
        }
    }

    internal static partial class HlsRegexPatterns
    {
        [GeneratedRegex(@"^\[hls @ \S+\] Opening '.*?\.ts'")]
        public static partial Regex WritingHlsSegment();
    }
}
