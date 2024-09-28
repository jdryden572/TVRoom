using Microsoft.IO;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace TVRoom.Broadcast
{
    public sealed class HlsLiveStream : IDisposable
    {
        private readonly HlsConfiguration _hlsConfig; 
        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<HlsStreamFile> _channel = Channel.CreateBounded<HlsStreamFile>(new BoundedChannelOptions(50));
        private readonly ConcurrentDictionary<string, HlsStreamFile> _files = new();

        public HlsLiveStream(HlsConfiguration hlsConfig)
        {
            _hlsConfig = hlsConfig;
            _ = Task.Run(ListenForStreamFilesAsync);
        }

        public async Task IngestStreamFileAsync(string fileName, PipeReader fileContents)
        {
            if (IsInvalidFileName(fileName))
            {
                throw new ArgumentException("File name must not contain path separators", nameof(fileName));
            }

            Console.WriteLine($"{fileName}: starting");
            var stopwatch = Stopwatch.StartNew();
            var hlsStreamFile = await HlsStreamFile.ReadAsync(fileName, fileContents);
            stopwatch.Stop();
            Console.WriteLine($"{fileName}: {hlsStreamFile.Length} bytes ingested in {stopwatch.Elapsed.TotalMilliseconds} ms");

            await _channel.Writer.WriteAsync(hlsStreamFile);
        }

        public bool TryGetFile(string fileName, [MaybeNullWhen(false)] out HlsStreamFile hlsStreamFile) =>
            _files.TryGetValue(fileName, out hlsStreamFile);

        private async Task ListenForStreamFilesAsync()
        {
            var segmentQueue = new Queue<HlsStreamFile>();

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var file = await _channel.Reader.ReadAsync(_cts.Token);

                    if (file.FileType == HlsFileType.Segment)
                    {
                        _files.TryAdd(file.FileName, file);
                        segmentQueue.Enqueue(file);

                        if (segmentQueue.Count > _hlsConfig.HlsListSize + 2)
                        {
                            using var toDispose = segmentQueue.Dequeue();
                            _files.TryRemove(toDispose.FileName, out _);
                        }
                    }
                    else
                    {
                        if (_files.TryRemove(file.FileName, out var previousFile))
                        {
                            previousFile.Dispose();
                        }

                        _files.TryAdd(file.FileName, file);
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

        private static bool IsInvalidFileName(string fileName) => fileName.AsSpan().ContainsAny(Path.GetInvalidFileNameChars());

        public void Dispose()
        {
            _cts.Cancel();
            _channel.Writer.Complete();
            var filesToDispose = _files.Keys.ToArray();
            foreach (var file in filesToDispose)
            {
                if (_files.TryRemove(file, out var toDispose))
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
        private byte[]? _buffer;
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
                "live.m3u8" => HlsFileType.MasterPlaylist,
                _ => HlsFileType.Segment,
            };

            if (fileType == HlsFileType.Segment && Path.GetExtension(fileName) != ".ts")
            {
                throw new ArgumentException("Unrecognized file extension in HLS file!", nameof(fileName));
            }

            HlsStreamFile streamFile;
            while (true)
            {
                var result = await fileContents.ReadAsync();
                if (result.IsCompleted)
                {
                    var length = (int)result.Buffer.Length;
                    var buffer = FileBufferPool.Rent(length);
                    result.Buffer.CopyTo(buffer);
                    streamFile = new HlsStreamFile(fileName, fileType, buffer, length);
                    break;
                }

                fileContents.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            }

            await fileContents.CompleteAsync();
            return streamFile;
        }

        private HlsStreamFile(string fileName, HlsFileType fileType, byte[] buffer, int length)
        {
            FileName = fileName;
            FileType = fileType;
            Length = length;
            _buffer = buffer;
        }

        public IResult GetResult()
        {
            ReadOnlyMemory<byte> memory;
            lock (_lock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(FileName);
                }

                _refCount++;
                memory = new ReadOnlyMemory<byte>(_buffer, 0, Length);
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
            byte[]? bufferToReturn = null;
            lock (_lock)
            {
                if (_disposed && _refCount == 0)
                {
                    bufferToReturn = _buffer;
                    _buffer = null;
                }
            }

            if (bufferToReturn is not null)
            {
                FileBufferPool.Return(bufferToReturn);
            }
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

                var contentType = 

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

    public static class FileBufferPool
    {
        private static long _rentedBufferCount;
        private static long _rentedBufferLength;

        public static byte[] Rent(int minimumLength)
        {
            var array = ArrayPool<byte>.Shared.Rent(minimumLength);
            Interlocked.Increment(ref _rentedBufferCount);
            Interlocked.Add(ref _rentedBufferLength, array.LongLength);
            //Report();
            return array;
        }

        public static void Return(byte[] array)
        {
            Interlocked.Decrement(ref _rentedBufferCount);
            Interlocked.Add(ref _rentedBufferLength, -1 * array.LongLength);
            ArrayPool<byte>.Shared.Return(array);
            //Report();
        }

        private static void Report()
        {
            var count = Interlocked.Read(ref _rentedBufferCount);
            var length = Interlocked.Read(ref _rentedBufferLength) / (1024.0 * 1024.0);
            Console.WriteLine($"Rented buffers count={count} size={length} MB");
        }
    }
}
