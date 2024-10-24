using TVRoom.Configuration;

namespace TVRoom.HLS
{
    public sealed class HlsTranscodeFactory
    {
        private readonly HlsTranscodeStore _store;
        private readonly HlsConfiguration _hlsConfig;
        private readonly TranscodeConfigService _transcodeConfigService;

        public HlsTranscodeFactory(HlsTranscodeStore store, HlsConfiguration hlsConfig, TranscodeConfigService transcodeConfigService)
        {
            _store = store;
            _hlsConfig = hlsConfig;
            _transcodeConfigService = transcodeConfigService;
        }

        public async Task<HlsTranscode> Create(string channelUrl, ILogger logger)
        {
            var transcodeConfig = await _transcodeConfigService.GetLatestConfig();
            var transcode = new HlsTranscode(_store, _hlsConfig, logger, channelUrl, transcodeConfig);
            _store.Add(transcode);
            return transcode;
        }
    }
}
