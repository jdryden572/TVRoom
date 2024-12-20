﻿using CommunityToolkit.HighPerformance.Buffers;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace TVRoom.HLS
{

    public interface IBufferLease : IDisposable
    {
        ReadOnlySpan<byte> GetSpan();
        ReadOnlyMemory<byte> GetMemory();
    }

    public sealed class InterlockedLong
    {
        private long _value;
        public long Value
        {
            get => Interlocked.CompareExchange(ref _value, 0, 0);
            set => Interlocked.Exchange(ref _value, value);
        }

        public long Add(long value) => Interlocked.Add(ref _value, value);
    }

    [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "Called indirectly by disposal")]
    public sealed partial class SharedBuffer : IDisposable
    {
        public static InterlockedLong RentedBytes { get; } = new();
        public static InterlockedLong RentedBufferCount { get; } = new();

        public static long MaxFileLength { get; } = 10 * 1024 * 1024; // 10MB
        private static readonly ArrayPool<byte> _largePool = ArrayPool<byte>.Create((int)MaxFileLength, 20);

        private readonly object _lock = new();
        private readonly ILogger _logger;
        private readonly ScopedBufferPool _pool;

        // Synchronize access to these with the lock
        private MemoryOwner<byte> _buffer;
        private int _refCount;
        private bool _disposed;

        private SharedBuffer(MemoryOwner<byte> buffer, ILogger logger, ScopedBufferPool pool)
        {
            _buffer = buffer;
            _logger = logger;
            _pool = pool;

            LogBufferAllocated(_buffer.Length, Id);
        }

        public static SharedBuffer Create(ReadOnlySequence<byte> source, ILogger logger, ScopedBufferPool pool)
        {
            var buffer = MemoryOwner<byte>.Allocate((int)source.Length, _largePool);
            RentedBytes.Add(source.Length);
            RentedBufferCount.Add(1);

            source.CopyTo(buffer.Span);
            return new SharedBuffer(buffer, logger, pool);
        }

        ~SharedBuffer()
        {
            LogSharedBufferFinalized(_buffer.Length, Id);
            _buffer.Dispose();
#if DEBUG
            Debug.Fail($"Shared buffer is being finalized size={_buffer.Length} id='{Id}'");
#endif
        }

        public Guid Id { get; } = Guid.NewGuid();

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
                ObjectDisposedException.ThrowIf(_disposed, this);

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
                _pool.BufferReturned(this);

                var size = bufferToReturn.Length;
                GC.SuppressFinalize(this);
                bufferToReturn.Dispose();

                RentedBytes.Add(-1 * size);
                RentedBufferCount.Add(-1);
                LogBufferReturned(bufferToReturn.Length, Id);
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
                ObjectDisposedException.ThrowIf(_sharedBuffer is null, this);
                return _sharedBuffer._buffer.Span;
            }

            public ReadOnlyMemory<byte> GetMemory()
            {
                ObjectDisposedException.ThrowIf(_sharedBuffer is null, this);
                return _sharedBuffer._buffer.Memory;
            }

            public void Dispose()
            {
                _sharedBuffer?.ReaderDisposed();
                _sharedBuffer = null;
            }
        }

        [LoggerMessage(Level = LogLevel.Trace, Message = "Allocated shared buffer size={Size} id='{Id}'")]
        private partial void LogBufferAllocated(int size, Guid id);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Returned shared buffer size={Size} id='{Id}'")]
        private partial void LogBufferReturned(int size, Guid id);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Shared buffer is being finalized size={Size} id='{Id}'")]
        private partial void LogSharedBufferFinalized(int size, Guid id);
    }
}
