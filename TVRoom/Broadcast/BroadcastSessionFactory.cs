using TVRoom.Tuner;
using System.Security.Cryptography;
using TVRoom.Transcode;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastSessionFactory
    {
        private readonly HlsTranscodeManager _transcodeManager;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILoggerFactory _loggerFactory;

        public BroadcastSessionFactory(HlsConfiguration hlsConfig, ILoggerFactory loggerFactory, HlsTranscodeManager transcodeManager)
        {
            _hlsConfig = hlsConfig;
            _loggerFactory = loggerFactory;
            _transcodeManager = transcodeManager;
        }

        public BroadcastSession CreateBroadcast(ChannelInfo channelInfo)
        {
            // Generate unique session and save broadcast info (usable to watch the stream)
            var sessionId = GenerateSessionId();

            var logger = _loggerFactory.CreateLogger($"Broadcast-{sessionId}");
            var liveStream = new HlsLiveStream(channelInfo.Url, _transcodeManager, logger, _hlsConfig);
            var broadcastInfo = new BroadcastInfo(channelInfo, sessionId);
            return new BroadcastSession(broadcastInfo, liveStream, _hlsConfig, logger);
        }

        private string GenerateSessionId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }
    }
}
