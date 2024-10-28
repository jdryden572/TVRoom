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

        public BroadcastSessionFactory(
            HlsConfiguration hlsConfig,
            ILoggerFactory loggerFactory,
            TranscodeSessionManager transcodeSessionManager,
            TunerStatusProvider tunerStatusProvider)
        {
            _hlsConfig = hlsConfig;
            _loggerFactory = loggerFactory;
            _transcodeSessionManager = transcodeSessionManager;
            _tunerStatusProvider = tunerStatusProvider;
        }

        public async Task<BroadcastSession> CreateBroadcast(ChannelInfo channelInfo)
        {
            // Generate unique session and save broadcast info (usable to watch the stream)
            var sessionId = GenerateSessionId();

            var logger = _loggerFactory.CreateLogger($"Broadcast-{sessionId}");
            var transcode = await _transcodeSessionManager.CreateTranscode(channelInfo.Url, logger);
            var broadcastInfo = new BroadcastInfo(channelInfo, sessionId, transcode.FFmpegProcess.Arguments);
            return new BroadcastSession(broadcastInfo, transcode, _transcodeSessionManager, _tunerStatusProvider, _hlsConfig, logger);
        }

        private string GenerateSessionId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }
    }
}
