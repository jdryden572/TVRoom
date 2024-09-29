using Microsoft.Toolkit.HighPerformance.Buffers;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace TVRoom.Broadcast
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

    public enum HlsFileType
    {
        MasterPlaylist,
        Playlist,
        Segment,
    }

    public sealed class HlsStreamFile : IDisposable
    {
        private readonly object _lock = new();

        // Synchronize access to these with the lock
        private MemoryOwner<byte>? _buffer;
        private int _refCount;
        private bool _disposed;

        public string FileName { get; }
        public HlsFileType FileType { get; }
        public int Length { get; }

        public static async Task<HlsStreamFile> ReadAsync(string fileName, PipeReader fileContents)
        {
            var fileType = fileName switch
            {
                "master.m3u8" => HlsFileType.MasterPlaylist,
                "live.m3u8" => HlsFileType.Playlist,
                _ => HlsFileType.Segment,
            };

            if (fileType == HlsFileType.Segment && Path.GetExtension(fileName) != ".ts")
            {
                throw new ArgumentException("Unrecognized file extension in HLS file!", nameof(fileName));
            }

            var buffer = await fileContents.PooledReadToEndAsync();

            var streamFile = new HlsStreamFile(fileName, fileType, buffer);
            return streamFile;
        }

        private HlsStreamFile(string fileName, HlsFileType fileType, MemoryOwner<byte> buffer)
        {
            FileName = fileName;
            FileType = fileType;
            _buffer = buffer;
            Length = buffer.Length;
        }

        public IResult GetResult()
        {
            ReadOnlyMemory<byte> memory;
            lock (_lock)
            {
                if (_disposed || _buffer is null)
                {
                    throw new ObjectDisposedException(FileName);
                }

                _refCount++;
                memory = _buffer.Memory;
            }

            return new HlsFileResult(this, memory);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
            }

            ReturnBufferIfDisposedAndRefCountZero();
        }

        private void ReaderDisposed()
        {
            lock (_lock)
            {
                _refCount--;
            }

            ReturnBufferIfDisposedAndRefCountZero();
        }

        private void ReturnBufferIfDisposedAndRefCountZero()
        {
            MemoryOwner<byte>? bufferToReturn = null;
            lock (_lock)
            {
                if (_disposed && _refCount == 0)
                {
                    bufferToReturn = _buffer;
                    _buffer = null;
                }
            }

            bufferToReturn?.Dispose();
        }
        

        private sealed class HlsFileResult : IResult
        {
            private readonly HlsStreamFile _streamFile;
            private readonly ReadOnlyMemory<byte> _buffer;
            private bool _finished;

            public HlsFileResult(HlsStreamFile streamFile, ReadOnlyMemory<byte> buffer)
            {
                _streamFile = streamFile;
                _buffer = buffer;
            }

            public async Task ExecuteAsync(HttpContext httpContext)
            {
                if (_finished)
                {
                    throw new InvalidOperationException("Cannot execute HlsFileResult more than once!");
                }

                _finished = true;

                httpContext.Response.Headers.ContentType = _streamFile.FileType switch
                {
                    HlsFileType.Segment => "application/octet-stream",
                    _ => "audio/mpegurl",
                };

                try
                {
                    await httpContext.Response.BodyWriter.WriteAsync(_buffer);
                }
                finally
                {
                    _streamFile.ReaderDisposed();
                }
            }
        }
    }
}
