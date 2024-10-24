using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using TVRoom.Configuration;

namespace TVRoom.HLS
{
    public sealed class HlsTranscodeManager
    {
        private readonly ConcurrentDictionary<string, HlsTranscode> _transcodes = new();
        private readonly TranscodeConfigService _transcodeConfigService;
        private readonly HlsConfiguration _hlsConfig;

        public HlsTranscodeManager(TranscodeConfigService transcodeConfigService, HlsConfiguration hlsConfig)
        {
            _transcodeConfigService = transcodeConfigService;
            _hlsConfig = hlsConfig;
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
