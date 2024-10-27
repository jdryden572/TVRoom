using System.IO.Pipelines;
using TVRoom.HLS;

namespace TVRoom.Broadcast
{
    public static class BodyReaderExtensions
    {
        public static async Task<SharedBuffer> ReadToSharedBufferAsync(this PipeReader reader)
        {
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
                        var sharedBuffer = SharedBuffer.Create(buffer);
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
    }
}
