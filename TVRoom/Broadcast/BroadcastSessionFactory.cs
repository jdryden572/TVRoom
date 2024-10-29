using TVRoom.Tuner;
using System.Security.Cryptography;
using TVRoom.Transcode;
using TVRoom.Configuration;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastSessionFactory
    {
        private readonly TranscodeSessionManager _transcodeSessionManager;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILoggerFactory _loggerFactory;
        private readonly TunerStatusProvider _tunerStatusProvider;
        private readonly BroadcastHistoryService _broadcastHistoryService;

        public BroadcastSessionFactory(
            HlsConfiguration hlsConfig,
            ILoggerFactory loggerFactory,
            TranscodeSessionManager transcodeSessionManager,
            TunerStatusProvider tunerStatusProvider,
            BroadcastHistoryService broadcastHistoryService)
        {
            _hlsConfig = hlsConfig;
            _loggerFactory = loggerFactory;
            _transcodeSessionManager = transcodeSessionManager;
            _tunerStatusProvider = tunerStatusProvider;
            _broadcastHistoryService = broadcastHistoryService;
        }

        public async Task<BroadcastSession> CreateBroadcast(ChannelInfo channelInfo)
        {
            // Generate unique session and save broadcast info (usable to watch the stream)
            var sessionId = GenerateSessionId();

            var logger = _loggerFactory.CreateLogger($"Broadcast-{sessionId}");
            var transcode = await _transcodeSessionManager.CreateTranscode(channelInfo.Url, logger);
            var broadcastInfo = new BroadcastInfo(channelInfo, sessionId, transcode.FFmpegArguments);
            return new BroadcastSession(
                broadcastInfo,
                transcode,
                _transcodeSessionManager,
                _tunerStatusProvider,
                _broadcastHistoryService,
                _hlsConfig,
                logger);
        }

        private static string GenerateSessionId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }
    }
}
