using TVRoom.Tuner;
using System.Security.Cryptography;
using TVRoom.HLS;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastSessionFactory
    {
        private readonly HlsTranscodeFactory _ffmpegProcessFactory;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILoggerFactory _loggerFactory;

        public BroadcastSessionFactory(HlsConfiguration hlsConfig, ILoggerFactory loggerFactory, HlsTranscodeFactory ffmpegProcessFactory)
        {
            _hlsConfig = hlsConfig;
            _loggerFactory = loggerFactory;
            _ffmpegProcessFactory = ffmpegProcessFactory;
        }

        public async Task<BroadcastSession> CreateBroadcast(ChannelInfo channelInfo)
        {
            // Generate unique session and save broadcast info (usable to watch the stream)
            var sessionId = GenerateSessionId();

            // Create a directory to hold the transcoded HLS files
            var folder = _hlsConfig.BaseTranscodeDirectory.CreateSubdirectory(sessionId);

            var logger = _loggerFactory.CreateLogger($"Broadcast-{sessionId}");
            var process = await _ffmpegProcessFactory.Create(channelInfo.Url, logger);
            var broadcastInfo = new BroadcastInfo(channelInfo, sessionId, process.FFmpegProcess.Arguments);
            return new BroadcastSession(broadcastInfo, folder, process, _hlsConfig, logger);
        }

        private string GenerateSessionId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }
    }
}
