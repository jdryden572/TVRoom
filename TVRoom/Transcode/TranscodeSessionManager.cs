using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using TVRoom.Configuration;
using TVRoom.HLS;

namespace TVRoom.Transcode
{
    public sealed class TranscodeSessionManager
    {
        private readonly ConcurrentDictionary<string, HlsFileIngester> _fileIngesters = new();
        private readonly HlsConfiguration _hlsConfig;
        private readonly TranscodeConfigService _transcodeConfigService;

        public TranscodeSessionManager(HlsConfiguration hlsConfig, TranscodeConfigService transcodeConfigService)
        {
            _hlsConfig = hlsConfig;
            _transcodeConfigService = transcodeConfigService;
        }

        public async Task<TranscodeSession> CreateTranscode(string channelUrl, ILogger logger)
        {
            var transcodeConfig = await _transcodeConfigService.GetLatestConfig();
            var transcode = new TranscodeSession(this, _hlsConfig, logger, channelUrl, transcodeConfig);
            _fileIngesters.TryAdd(transcode.Id, transcode.FileIngester);
            return transcode;
        }

        public bool TryGetFileIngester(string transcodeId, [MaybeNullWhen(false)] out HlsFileIngester ingester) => 
            _fileIngesters.TryGetValue(transcodeId, out ingester);

        public void Remove(string transcodeId) => _fileIngesters.TryRemove(transcodeId, out _);
    }
}
