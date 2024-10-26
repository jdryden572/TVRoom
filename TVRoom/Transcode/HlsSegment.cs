using CommunityToolkit.HighPerformance.Buffers;
using System.Diagnostics;

namespace TVRoom.Transcode
{
    public sealed class HlsSegment : IDisposable
    {
        private readonly object _lock = new();

        // Synchronize access to these with the lock
        private MemoryOwner<byte>? _buffer;
        private int _refCount;
        private bool _disposed;

        public string FileName { get; }
        public int Length => _buffer?.Length ?? 0;

        public HlsSegment(string fileName, MemoryOwner<byte> buffer)
        {
            FileName = fileName;
            _buffer = buffer;
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

            return new HlsSegmentResult(this, memory);
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
        ~HlsSegment()
        {
            Debug.Fail($"HlsSegement is being finalized! '{FileName}'");
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


        private sealed class HlsSegmentResult : IResult
        {
            private readonly HlsSegment _hlsSegment;
            private readonly ReadOnlyMemory<byte> _buffer;
            private bool _finished;

            public HlsSegmentResult(HlsSegment hlsSegment, ReadOnlyMemory<byte> buffer)
            {
                _hlsSegment = hlsSegment;
                _buffer = buffer;
            }

            public async Task ExecuteAsync(HttpContext httpContext)
            {
                if (_finished)
                {
                    throw new InvalidOperationException("Cannot execute HlsSegmentResult more than once!");
                }

                _finished = true;

                httpContext.Response.Headers.ContentType = "application/octet-stream";

                try
                {
                    await httpContext.Response.BodyWriter.WriteAsync(_buffer);
                }
                finally
                {
                    _hlsSegment.ReaderDisposed();
                }
            }
        }
    }
}
