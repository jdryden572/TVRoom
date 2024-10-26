using System.Security.Cryptography;
using TVRoom.Broadcast;
using TVRoom.Configuration;

namespace TVRoom.HLS
{
    public sealed class HlsTranscode : IDisposable
    {
        private readonly HlsTranscodeManager _transcodeManager;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILogger _logger;

        public HlsTranscode(HlsTranscodeManager transcodeManager, HlsConfiguration hlsConfig, ILogger logger, string input, TranscodeConfigDto transcodeConfig)
        {
            _transcodeManager = transcodeManager;
            _hlsConfig = hlsConfig;
            _logger = logger;
            Id = GenerateTranscodeId();

            var hlsSettings = $"-f hls -hls_time {_hlsConfig.HlsTime} -hls_list_size {_hlsConfig.HlsListSize}";
            var playlist = $"{_hlsConfig.HlsIngestBaseAddress}/{Id}/live.m3u8";
            var arguments = $"-y {RemoveNewLines(transcodeConfig.InputVideoParameters)} -i {input} -c:a aac -ac 2 {RemoveNewLines(transcodeConfig.OutputVideoParameters)} {hlsSettings} -master_pl_name master.m3u8 {playlist}";

            FFmpegProcess = new FFmpegProcess(_hlsConfig.FFmpeg.FullName, arguments, logger);
            //FFmpegProcess.FFmpegOutput.WriteTranscodeLogsToFile(broadcastInfo, hlsConfig);
            FileIngester = new();
        }

        public string Id { get; }

        public FFmpegProcess FFmpegProcess { get; }

        public HlsFileIngester FileIngester { get; }

        public void Start()
        {
            _logger.LogWarning("Starting transcode session {Id}", Id);
            FFmpegProcess.Start();
        }

        public async Task StopAsync()
        {
            _logger.LogWarning("Stopping transcode session {Id}", Id);
            await FFmpegProcess.StopAsync();
            _logger.LogWarning("Finished stopping transcode session {Id}", Id);
        }

        private static string GenerateTranscodeId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return "transcode_" + RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }

        private static string RemoveNewLines(string input)
        {
            var parts = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', parts);
        }

        public void Dispose()
        {
            FFmpegProcess.Dispose();
            _transcodeManager.Remove(Id);
        }
    }
}
