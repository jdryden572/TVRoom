using LivingRoom.Tuner;
using System.Security.Cryptography;

namespace LivingRoom.Broadcast
{
    public sealed class BroadcastSessionFactory
    {
        private readonly TranscodeConfiguration _transcodeConfig;
        private readonly ILoggerFactory _loggerFactory;

        public BroadcastSessionFactory(TranscodeConfiguration transcodeConfig, ILoggerFactory loggerFactory)
        {
            _transcodeConfig = transcodeConfig;
            _loggerFactory = loggerFactory;
        }

        public BroadcastSession CreateBroadcast(ChannelInfo channelInfo, TranscodeOptions transcodeOptions)
        {
            // Generate unique session and save broadcast info (usable to watch the stream)
            var sessionId = GenerateSessionId();
            var broadcastInfo = new BroadcastInfo(channelInfo, sessionId);

            // Create a directory to hold the transcoded HLS files
            var folder = _transcodeConfig.BaseTranscodeDirectory.CreateSubdirectory(sessionId);

            // Start ffmpeg process
            var logger = _loggerFactory.CreateLogger($"Broadcast-{sessionId}");
            var arguments = BuildFFmpegArguments(channelInfo.Url, transcodeOptions, folder);
            var process = new FFmpegProcess(_transcodeConfig.FFmpeg.FullName, arguments, logger);

            return new BroadcastSession(broadcastInfo, folder, process, _transcodeConfig, logger);
        }

        private string GenerateSessionId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }

        private string BuildFFmpegArguments(string input, TranscodeOptions options, DirectoryInfo transcodeDirectory)
        {
            string deleteSegments = _transcodeConfig.HlsDeleteSegments ? "-hls_flags delete_segments" : string.Empty;
            var hlsSettings = $"-f hls -hls_time {_transcodeConfig.HlsTime} -hls_list_size {_transcodeConfig.HlsListSize} {deleteSegments}";

            var playlist = Path.Join(transcodeDirectory.FullName, _transcodeConfig.HlsPlaylistName);
            return $"-y {options.InputVideoOptions} -i {input} -c:a aac -ac 2 {options.OutputVideoOptions} {hlsSettings} {playlist}";
        }
    }
}
