using Microsoft.Extensions.Options;

namespace LivingRoom.Broadcast
{
    public sealed class TranscodeConfiguration
    {
        public TranscodeConfiguration(IOptions<HlsTranscodeOptions> options, IHostApplicationLifetime appLifetime)
        {
            var hlsOptions = options.Value;
            
            FFmpeg = new FileInfo(hlsOptions.FFmpegPath);
            if (!FFmpeg.Exists)
            {
                throw new ArgumentException($"FFmpeg not found at path '{hlsOptions.FFmpegPath}'");
            }

            BaseTranscodeDirectory = new DirectoryInfo(hlsOptions.TranscodeDirectory);
            if (!BaseTranscodeDirectory.Exists)
            {
                throw new ArgumentException($"Transcode directory not found at '{hlsOptions.TranscodeDirectory}'");
            }

            HlsTime = hlsOptions.HlsTime;
            HlsListSize = hlsOptions.HlsListSize;
            HlsDeleteSegments = hlsOptions.HlsDeleteSegments;
            HlsPlaylistName = hlsOptions.HlsPlaylistName;
            HlsPlaylistReadyCount = hlsOptions.HlsPlaylistReadyCount;
            ApplicationStopping = appLifetime.ApplicationStopping;
        }

        public FileInfo FFmpeg { get; }
        public DirectoryInfo BaseTranscodeDirectory { get; }
        public int HlsTime { get; }
        public int HlsListSize { get; }
        public bool HlsDeleteSegments { get; }
        public string HlsPlaylistName { get; }
        public int HlsPlaylistReadyCount { get; }
        public CancellationToken ApplicationStopping { get; }
    }

    public class HlsTranscodeOptions
    {
        public const string SectionName = "TranscodeOptions";

        public required string FFmpegPath { get; init; }

        public required string TranscodeDirectory { get; init; }

        public int HlsTime { get; init; } = 2;

        public int HlsListSize { get; init; } = 5;

        public bool HlsDeleteSegments { get; init; } = true;

        public string HlsPlaylistName { get; init; } = "live.m3u8";

        public int HlsPlaylistReadyCount { get; init; } = 2;
    }
}
