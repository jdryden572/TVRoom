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

    public sealed class SharedBuffer : IDisposable
    {
        private static readonly long _maxFileLength = 10 * 1024 * 1024; // 10MB
        private static readonly ArrayPool<byte> _largePool = ArrayPool<byte>.Create((int)_maxFileLength, 20);

        private readonly object _lock = new();

        // Synchronize access to these with the lock
        private MemoryOwner<byte> _buffer;
        private int _refCount;
        private bool _disposed;

        private SharedBuffer(MemoryOwner<byte> buffer)
        {
            _buffer = buffer;
        }

#if DEBUG
        ~SharedBuffer()
        {
            Debug.Fail($"SharedBuffer is being finalized!");
        }
#endif

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

        public static SharedBuffer Create(ReadOnlySequence<byte> source)
        {
            var buffer = MemoryOwner<byte>.Allocate((int)source.Length, _largePool);
            source.CopyTo(buffer.Span);
            return new SharedBuffer(buffer);
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
                GC.SuppressFinalize(this);
                bufferToReturn.Dispose();
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
    }
}
