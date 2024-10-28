using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using TVRoom.Configuration;
using TVRoom.Helpers;
using TVRoom.HLS;

namespace TVRoom.Transcode
{
    public sealed class TranscodeSession : IDisposable
    {
        private readonly TranscodeSessionManager _transcodeManager;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILogger _logger;
        private readonly FFmpegProcess _ffmpegProcess;

        public TranscodeSession(TranscodeSessionManager transcodeManager, HlsConfiguration hlsConfig, ILogger logger, string input, TranscodeConfigDto transcodeConfig)
        {
            _transcodeManager = transcodeManager;
            _hlsConfig = hlsConfig;
            _logger = logger;
            Id = GenerateTranscodeId();

            var hlsSettings = $"-f hls -hls_time {_hlsConfig.HlsTime} -hls_list_size {_hlsConfig.HlsListSize}";
            var playlist = $"{_hlsConfig.HlsIngestBaseAddress}/{Id}/live.m3u8";
            var arguments = $"-y {RemoveNewLines(transcodeConfig.InputVideoParameters)} -i {input} -c:a aac -ac 2 {RemoveNewLines(transcodeConfig.OutputVideoParameters)} {hlsSettings} -master_pl_name master.m3u8 {playlist}";

            _ffmpegProcess = new FFmpegProcess(_hlsConfig.FFmpeg.FullName, arguments, logger);

            Stats = _ffmpegProcess.FFmpegOutput.ConditionalMap<string, TranscodeStats>((line, onNext) =>
            {
                if (TranscodeStats.TryParse(line, out var stats))
                {
                    onNext(stats);
                }
            });
        }

        public string Id { get; }

        public string FFmpegArguments => _ffmpegProcess.Arguments;

        public IObservable<string> FFmpegOutput => _ffmpegProcess.FFmpegOutput;

        public IObservable<TranscodeStats> Stats { get; }

        public HlsFileIngester FileIngester { get; } = new();

        public void Start()
        {
            _logger.LogWarning("Starting transcode session {Id}", Id);
            _ffmpegProcess.Start();
        }

        public async Task StopAsync()
        {
            _logger.LogWarning("Stopping transcode session {Id}", Id);
            await _ffmpegProcess.StopAsync();
            _logger.LogWarning("Finished stopping transcode session {Id}", Id);
        }

        private static string GenerateTranscodeId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }

        private static string RemoveNewLines(string input)
        {
            var parts = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', parts);
        }

        public void Dispose()
        {
            _ffmpegProcess.Dispose();
            _transcodeManager.Remove(Id);
        }
    }
}
