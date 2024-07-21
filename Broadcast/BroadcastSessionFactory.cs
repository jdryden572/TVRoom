using LivingRoom.Configuration;
using LivingRoom.Tuner;
using System.Security.Cryptography;

namespace LivingRoom.Broadcast
{
    public sealed class BroadcastSessionFactory
    {
        private readonly HlsConfiguration _hlsConfig;
        private readonly TranscodeConfigService _transcodeConfigService;
        private readonly ILoggerFactory _loggerFactory;

        public BroadcastSessionFactory(HlsConfiguration hlsConfig, TranscodeConfigService transcodeConfigService, ILoggerFactory loggerFactory)
        {
            _hlsConfig = hlsConfig;
            _transcodeConfigService = transcodeConfigService;
            _loggerFactory = loggerFactory;
        }

        public async Task<BroadcastSession> CreateBroadcast(ChannelInfo channelInfo)
        {
            // Generate unique session and save broadcast info (usable to watch the stream)
            var sessionId = GenerateSessionId();

            // Create a directory to hold the transcoded HLS files
            var folder = _hlsConfig.BaseTranscodeDirectory.CreateSubdirectory(sessionId);

            // Start ffmpeg process
            var logger = _loggerFactory.CreateLogger($"Broadcast-{sessionId}");
            var arguments = await BuildFFmpegArguments(channelInfo.Url, folder);
            var broadcastInfo = new BroadcastInfo(channelInfo, sessionId, arguments);
            var process = new FFmpegProcess(_hlsConfig.FFmpeg.FullName, arguments, logger);

            return new BroadcastSession(broadcastInfo, folder, process, _hlsConfig, logger);
        }

        private string GenerateSessionId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }

        private async Task<string> BuildFFmpegArguments(string input, DirectoryInfo transcodeDirectory)
        {
            var transcodeConfig = await _transcodeConfigService.GetLatestConfig();

            var hlsSettings = $"-f hls -hls_time {_hlsConfig.HlsTime} -hls_list_size {_hlsConfig.HlsListSize} -hls_flags delete_segments";

            var playlist = Path.Join(transcodeDirectory.FullName, "live.m3u8");
            return $"-y {RemoveNewLines(transcodeConfig.InputVideoParameters)} -i {input} -c:a aac -ac 2 {RemoveNewLines(transcodeConfig.OutputVideoParameters)} {hlsSettings} -master_pl_name master.m3u8 {playlist}";
        }

        private string RemoveNewLines(string input)
        {
            var parts = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', parts);
        }
    }
}
