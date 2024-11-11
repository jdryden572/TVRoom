using System.Security.Cryptography;
using TVRoom.Configuration;
using TVRoom.Helpers;
using TVRoom.HLS;

namespace TVRoom.Transcode
{
    public sealed partial class TranscodeSession : IDisposable
    {
        private readonly TranscodeSessionManager _transcodeManager;
        private readonly HlsConfiguration _hlsConfig;
        private readonly ILogger _logger;
        private readonly FFmpegProcess _ffmpegProcess;

        public TranscodeSession(TranscodeSessionManager transcodeManager, HlsConfiguration hlsConfig, ILogger logger, string input, TranscodeConfigDto transcodeConfig, ScopedBufferPool bufferPool)
        {
            _transcodeManager = transcodeManager;
            _hlsConfig = hlsConfig;
            _logger = logger;
            Id = GenerateTranscodeId();

            BufferPool = bufferPool;
            FileIngester = new(bufferPool);

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

        public ScopedBufferPool BufferPool { get; }

        public HlsFileIngester FileIngester { get; }

        public void Start()
        {
            LogStartingTranscode(Id);
            _ffmpegProcess.Start();
        }

        public async Task StopAsync()
        {
            LogStoppingTranscode(Id);
            await _ffmpegProcess.StopAsync();
            LogTranscodeStopped(Id);
        }

        private static string GenerateTranscodeId()
        {
            const string sessionIdCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";
            return RandomNumberGenerator.GetString(sessionIdCharacters, 32);
        }

        private static readonly char[] separator = ['\r', '\n'];
        private static string RemoveNewLines(string input)
        {
            var parts = input.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', parts);
        }

        public void Dispose()
        {
            _ffmpegProcess.Dispose();
            _transcodeManager.Remove(Id);
            FileIngester.Dispose();
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "Starting transcode session {Id}")]
        private partial void LogStartingTranscode(string id);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping transcode session {Id}")]
        private partial void LogStoppingTranscode(string id);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Transcode session {Id} stopped")]
        private partial void LogTranscodeStopped(string id);
    }
}
