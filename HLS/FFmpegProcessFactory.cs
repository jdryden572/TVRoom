using TVRoom.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace TVRoom.HLS
{
    public sealed class FFmpegProcessFactory
    {
        private readonly HlsConfiguration _hlsConfig;
        private readonly TranscodeConfigService _transcodeConfigService;
        private readonly string _serverAddress;

        public FFmpegProcessFactory(HlsConfiguration hlsConfig, TranscodeConfigService transcodeConfigService, IServer server)
        {
            _hlsConfig = hlsConfig;
            _transcodeConfigService = transcodeConfigService;

            var serverAddressesFeature = server.Features.Get<IServerAddressesFeature>() ?? throw new InvalidOperationException($"Missing feature {nameof(IServerAddressesFeature)}");
            var serverAddress = serverAddressesFeature.Addresses.FirstOrDefault() ?? throw new InvalidOperationException($"No address returned from {nameof(IServerAddressesFeature)}");
            var uri = new Uri(serverAddress);
            _serverAddress = $"{uri.Scheme}://127.0.0.1:{uri.Port}";
        }

        public async Task<FFmpegProcess> Create(string channelUrl, DirectoryInfo folder, ILogger logger)
        {
            var arguments = await BuildFFmpegArguments(channelUrl, folder);
            return new FFmpegProcess(_hlsConfig.FFmpeg.FullName, arguments, logger);
        }

        private async Task<string> BuildFFmpegArguments(string input, DirectoryInfo transcodeDirectory)
        {
            var transcodeConfig = await _transcodeConfigService.GetLatestConfig();

            var hlsSettings = $"-f hls -hls_time {_hlsConfig.HlsTime} -hls_list_size {_hlsConfig.HlsListSize}";

            var playlist = $"{_serverAddress}/streams/{transcodeDirectory.Name}/live.m3u8";
            return $"-y {RemoveNewLines(transcodeConfig.InputVideoParameters)} -i {input} -c:a aac -ac 2 {RemoveNewLines(transcodeConfig.OutputVideoParameters)} {hlsSettings} -master_pl_name master.m3u8 {playlist}";
        }

        private string RemoveNewLines(string input)
        {
            var parts = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', parts);
        }
    }
}
