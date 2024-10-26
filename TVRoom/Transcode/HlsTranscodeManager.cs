using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using TVRoom.Configuration;

namespace TVRoom.Transcode
{
    public sealed class HlsTranscodeManager
    {
        private readonly ConcurrentDictionary<string, HlsTranscode> _transcodes = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly HlsConfiguration _hlsConfig;
        private readonly TranscodeConfigService _transcodeConfigService;

        public HlsTranscodeManager(IServiceProvider serviceProvider, HlsConfiguration hlsConfig, TranscodeConfigService transcodeConfigService)
        {
            _serviceProvider = serviceProvider;
            _hlsConfig = hlsConfig;
            _transcodeConfigService = transcodeConfigService;
        }

        public async Task<HlsTranscode> CreateTranscode(string channelUrl, ILogger logger)
        {
            var transcodeConfig = await _transcodeConfigService.GetLatestConfig();
            var transcode = new HlsTranscode(this, _hlsConfig, logger, channelUrl, transcodeConfig);
            _transcodes.TryAdd(transcode.Id, transcode);
            return transcode;
        }

        public bool TryGetTranscode(string transcodeId, [MaybeNullWhen(false)] out HlsTranscode transcode) => 
            _transcodes.TryGetValue(transcodeId, out transcode);

        public void Remove(string transcodeId) => _transcodes.TryRemove(transcodeId, out _);
    }
}
