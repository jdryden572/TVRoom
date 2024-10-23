using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace TVRoom.HLS
{
    public sealed class HlsLiveStream : IDisposable
    {
        private readonly HlsConfiguration _hlsConfig;
        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<HlsStreamFile> _channel = Channel.CreateBounded<HlsStreamFile>(new BoundedChannelOptions(50));
        private readonly ConcurrentDictionary<string, HlsStreamFile> _segments = new();

        // Access these with interlocked
        private HlsStreamFile? _masterPlaylist;
        private HlsStreamFile? _playlist;

        public HlsLiveStream(HlsConfiguration hlsConfig)
        {
            _hlsConfig = hlsConfig;
            _ = Task.Run(ListenForStreamFilesAsync);
        }

        public async Task IngestStreamFileAsync(string fileName, PipeReader fileContents)
        {
            Console.WriteLine($"{fileName}: starting");
            var stopwatch = Stopwatch.StartNew();
            var hlsStreamFile = await HlsStreamFile.ReadAsync(fileName, fileContents);
            stopwatch.Stop();
            Console.WriteLine($"{fileName}: {hlsStreamFile.Length} bytes ingested in {stopwatch.Elapsed.TotalMilliseconds} ms");

            await _channel.Writer.WriteAsync(hlsStreamFile);
        }

        public IResult? GetMasterPlaylist()
        {
            var streamFile = Interlocked.CompareExchange(ref _masterPlaylist, null, null);
            return streamFile?.GetResult();
        }

        public IResult? GetPlaylist()
        {
            var streamFile = Interlocked.CompareExchange(ref _playlist, null, null);
            return streamFile?.GetResult();
        }

        public IResult? GetSegment(string fileName) =>
            _segments.TryGetValue(fileName, out var streamFile) ? streamFile.GetResult() : null;

        private async Task ListenForStreamFilesAsync()
        {
            var segmentQueue = new Queue<HlsStreamFile>();

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var file = await _channel.Reader.ReadAsync(_cts.Token);
                    switch (file.FileType)
                    {
                        case HlsFileType.Segment:
                            _segments.TryAdd(file.FileName, file);
                            segmentQueue.Enqueue(file);

                            if (segmentQueue.Count > _hlsConfig.HlsListSize + _hlsConfig.HlsDeleteThreshold)
                            {
                                using var toDispose = segmentQueue.Dequeue();
                                _segments.TryRemove(toDispose.FileName, out _);
                            }
                            break;
                        case HlsFileType.MasterPlaylist:
                            // Swap the value, dispose of the old one 
                            Interlocked.Exchange(ref _masterPlaylist, file)?.Dispose();
                            break;
                        case HlsFileType.Playlist:
                            // Swap the value, dispose of the old one 
                            Interlocked.Exchange(ref _playlist, file)?.Dispose();
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in reader: {ex}");
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _channel.Writer.Complete();
            var filesToDispose = _segments.Keys.ToArray();
            foreach (var file in filesToDispose)
            {
                if (_segments.TryRemove(file, out var toDispose))
                {
                    toDispose.Dispose();
                }
            }
        }
    }
}
