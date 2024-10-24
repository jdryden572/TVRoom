using CommunityToolkit.HighPerformance.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using TVRoom.Broadcast;

namespace TVRoom.HLS
{
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

#if DEBUG
        ~HlsStreamFile()
        {
            Debug.Fail($"Stream file is being finalized! {FileType} '{FileName}'");
        }
#endif

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

            if (bufferToReturn is not null)
            {
                GC.SuppressFinalize(this);
                bufferToReturn.Dispose();
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
