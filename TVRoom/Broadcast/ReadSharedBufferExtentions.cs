using System.IO.Pipelines;
using TVRoom.HLS;

namespace TVRoom.Broadcast
{
    public static class ReadSharedBufferExtentions
    {
        public static async Task<SharedBuffer> ReadToSharedBufferAsync(this HttpRequest request, string identifier)
        {
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
                        var sharedBuffer = SharedBuffer.Create(buffer, logger, identifier);
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
