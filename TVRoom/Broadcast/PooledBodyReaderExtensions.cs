using CommunityToolkit.HighPerformance.Buffers;
using System.Buffers;
using System.IO.Pipelines;

namespace TVRoom.Broadcast
{
    public static class PooledBodyReaderExtensions
    {
        private static readonly long _maxFileLength = 10 * 1024 * 1024; // 10MB
        private static readonly ArrayPool<byte> _largePool = ArrayPool<byte>.Create((int)_maxFileLength, 20);

        public static async Task<MemoryOwner<byte>> PooledReadToEndAsync(this PipeReader reader)
        {
            await Console.Out.WriteLineAsync($"array poool: {_largePool.GetHashCode()}");
            try
            {
                while (true)
                {
                    var result = await reader.ReadAsync();
                    var buffer = result.Buffer;

                    if (buffer.Length > _maxFileLength)
                    {
                        reader.AdvanceTo(buffer.End);
                        throw new InvalidOperationException($"File was larger than max allowed size of {_maxFileLength}");
                    }

                    if (result.IsCompleted || result.IsCanceled)
                    {
                        var memoryOwner = MemoryOwner<byte>.Allocate((int)buffer.Length, _largePool);
                        buffer.CopyTo(memoryOwner.Span);
                        reader.AdvanceTo(buffer.End);
                        return memoryOwner;
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }
    }
}
