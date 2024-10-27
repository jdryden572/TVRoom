using CommunityToolkit.HighPerformance.Buffers;
using System.Buffers;
using System.Diagnostics;

namespace TVRoom.HLS
{
    public interface IBufferLease : IDisposable
    {
        ReadOnlySpan<byte> GetSpan();
        ReadOnlyMemory<byte> GetMemory();
    }

    public sealed partial class SharedBuffer : IDisposable
    {
        public static long RentedBytes = 0;
        public static int RentedBufferCount = 0;

        public static long MaxFileLength { get; } = 10 * 1024 * 1024; // 10MB
        private static readonly ArrayPool<byte> _largePool = ArrayPool<byte>.Create((int)MaxFileLength, 20);

        private readonly object _lock = new();
        private readonly string _identifier;
        private readonly ILogger? _logger;

        // Synchronize access to these with the lock
        private MemoryOwner<byte> _buffer;
        private int _refCount;
        private bool _disposed;

        private SharedBuffer(MemoryOwner<byte> buffer, string? identifier, ILogger? logger)
        {
            _buffer = buffer;
            _identifier = identifier ?? string.Empty;
            _logger = logger;

            if (_logger is not null)
                LogBufferAllocated(_logger, _buffer.Length, _identifier);
        }

        public static SharedBuffer Create(ReadOnlySequence<byte> source, string? identifier = null, ILogger? logger = null)
        {
            var buffer = MemoryOwner<byte>.Allocate((int)source.Length, _largePool);
            Interlocked.Add(ref RentedBytes, source.Length);
            Interlocked.Increment(ref RentedBufferCount);

            source.CopyTo(buffer.Span);
            return new SharedBuffer(buffer, identifier, logger);
        }

        ~SharedBuffer()
        {
            if (_logger is not null)
                LogSharedBufferFinalized(_logger, _buffer.Length, _identifier);
            _buffer.Dispose();
#if DEBUG
            Debug.Fail($"Shared buffer is being finalized size={_buffer.Length} id='{_identifier}'");
#endif
        }

        public bool IsBufferDisposed
        {
            get
            {
                lock (_lock)
                {
                    return _refCount == -1;
                }
            }
        }

        public IBufferLease Rent()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(SharedBuffer));
                }

                _refCount++;
                return new BufferLease(this);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
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
                    _refCount = -1;
                }
            }

            if (bufferToReturn is not null)
            {
                var size = bufferToReturn.Length;
                GC.SuppressFinalize(this);
                bufferToReturn.Dispose();

                Interlocked.Add(ref RentedBytes, -1 * size);
                Interlocked.Decrement(ref RentedBufferCount);
                if (_logger is not null)
                    LogBufferReturned(_logger, bufferToReturn.Length, _identifier);
            }
        }

        private void ReaderDisposed()
        {
            lock (_lock)
            {
                _refCount--;
            }

            ReturnBufferIfDisposedAndRefCountZero();
        }

        private sealed class BufferLease : IBufferLease
        {
            private SharedBuffer? _sharedBuffer;

            public BufferLease(SharedBuffer sharedBuffer) => _sharedBuffer = sharedBuffer;

            public ReadOnlySpan<byte> GetSpan()
            {
                if (_sharedBuffer is null)
                {
                    throw new ObjectDisposedException(nameof(BufferLease));
                }

                return _sharedBuffer._buffer.Span;
            }

            public ReadOnlyMemory<byte> GetMemory()
            {
                if (_sharedBuffer is null)
                {
                    throw new ObjectDisposedException(nameof(BufferLease));
                }

                return _sharedBuffer._buffer.Memory;
            }

            public void Dispose()
            {
                _sharedBuffer?.ReaderDisposed();
                _sharedBuffer = null;
            }
        }

        [LoggerMessage(Level = LogLevel.Trace, Message = "Allocated shared buffer size={size} id='{id}'")]
        private static partial void LogBufferAllocated(ILogger logger, int size, string id);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Returned shared buffer size={size} id='{id}'")]
        private static partial void LogBufferReturned(ILogger logger, int size, string id);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Shared buffer is being finalized size={size} id='{id}'")]
        private static partial void LogSharedBufferFinalized(ILogger logger, int size, string id);
    }
}
