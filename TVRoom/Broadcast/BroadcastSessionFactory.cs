using TVRoom.Tuner;
using System.Security.Cryptography;
using TVRoom.Transcode;
using TVRoom.Configuration;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastSessionFactory
    {
        private readonly TranscodeSessionManager _transcodeManager;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILoggerFactory _loggerFactory;

        public BroadcastSessionFactory(HlsConfiguration hlsConfig, ILoggerFactory loggerFactory, TranscodeSessionManager transcodeManager)
        {
            _hlsConfig = hlsConfig;
            _loggerFactory = loggerFactory;
            _transcodeManager = transcodeManager;
        }

        public async Task<BroadcastSession> CreateBroadcast(ChannelInfo channelInfo)
        {
            // Generate unique session and save broadcast info (usable to watch the stream)
            var sessionId = GenerateSessionId();

            var logger = _loggerFactory.CreateLogger($"Broadcast-{sessionId}");
            var broadcastInfo = new BroadcastInfo(channelInfo, sessionId);
            var transcode = await _transcodeManager.CreateTranscode(channelInfo.Url, logger);
            return new BroadcastSession(broadcastInfo, transcode, _transcodeManager, _hlsConfig, logger);
        }

        private string GenerateSessionId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }
    }
}
