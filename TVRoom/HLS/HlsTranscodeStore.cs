using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace TVRoom.HLS
{
    public sealed class HlsTranscodeStore
    {
        private readonly ConcurrentDictionary<string, HlsTranscode> _transcodes = new();

        public bool TryGetTranscode(string transcodeId, [MaybeNullWhen(false)] out HlsTranscode transcode) => 
            _transcodes.TryGetValue(transcodeId, out transcode);

        public void Add(HlsTranscode transcode) => _transcodes.TryAdd(transcode.Id, transcode);

        public void Remove(string transcodeId) => _transcodes.TryRemove(transcodeId, out _);
    }
}
