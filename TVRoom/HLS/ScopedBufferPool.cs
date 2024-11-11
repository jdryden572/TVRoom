using System.Collections.Concurrent;

namespace TVRoom.HLS
{
    public sealed class ScopedBufferPool : IDisposable
    {
        private readonly object _lock = new();
        private readonly ConcurrentDictionary<Guid, SharedBuffer> _liveBuffers = new();
        private bool _disposed;

        public async Task<SharedBuffer> ReadToSharedBufferAsync(HttpRequest request)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            var reader = request.BodyReader;
            var logger = request.HttpContext.RequestServices.GetRequiredService<ILogger<SharedBuffer>>();

            try
            {
                while (true)
                {
                    var result = await reader.ReadAsync();
                    var buffer = result.Buffer;

                    if (buffer.Length > SharedBuffer.MaxFileLength)
                    {
                        reader.AdvanceTo(buffer.End);
                        throw new InvalidOperationException($"File was larger than max allowed size of {SharedBuffer.MaxFileLength}");
                    }

                    if (result.IsCompleted || result.IsCanceled)
                    {
                        var sharedBuffer = SharedBuffer.Create(buffer, logger, this);
                        _liveBuffers.TryAdd(sharedBuffer.Id, sharedBuffer);
                        reader.AdvanceTo(buffer.End);
                        return sharedBuffer;
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }

        public void BufferReturned(SharedBuffer buffer)
        {
            _liveBuffers.TryRemove(buffer.Id, out _);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
            }

            foreach (var buffer in _liveBuffers.Values)
            {
                buffer.Dispose();
            }
        }
    }
}
