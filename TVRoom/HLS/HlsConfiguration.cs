using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace TVRoom.HLS
{
    /// <summary>
    /// Process-global configuration for HLS 
    /// </summary>
    public sealed class HlsConfiguration
    {
        public HlsConfiguration(IOptions<HlsTranscodeOptions> options, IHostApplicationLifetime appLifetime, IServer server)
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
            HlsDeleteThreshold = hlsOptions.HlsDeleteThreshold;
            HlsPlaylistReadyCount = hlsOptions.HlsPlaylistReadyCount;
            ApplicationStopping = appLifetime.ApplicationStopping;

            var serverAddressesFeature = server.Features.Get<IServerAddressesFeature>() ?? throw new InvalidOperationException($"Missing feature {nameof(IServerAddressesFeature)}");
            var serverAddress = serverAddressesFeature.Addresses.FirstOrDefault() ?? throw new InvalidOperationException($"No address returned from {nameof(IServerAddressesFeature)}");
            var uri = new Uri(serverAddress);
            HlsIngestBaseAddress = $"{uri.Scheme}://127.0.0.1:{uri.Port}/hls";
        }

        public FileInfo FFmpeg { get; }
        public DirectoryInfo BaseTranscodeDirectory { get; }
        public int HlsTime { get; }
        public int HlsListSize { get; }
        public int HlsDeleteThreshold { get; }
        public int HlsPlaylistReadyCount { get; }
        public CancellationToken ApplicationStopping { get; }
        public string HlsIngestBaseAddress { get; }
    }

    public class HlsTranscodeOptions
    {
        public const string SectionName = "TranscodeOptions";

        public required string FFmpegPath { get; init; }

        public required string TranscodeDirectory { get; init; }

        public int HlsTime { get; init; } = 2;

        public int HlsListSize { get; init; } = 5;

        /// <summary>
        /// How many unreferenced segments to keep around before deleting them
        /// </summary>
        public int HlsDeleteThreshold { get; } = 2;

        public int HlsPlaylistReadyCount { get; init; } = 2;
    }
}
