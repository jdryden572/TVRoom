using System.IO.Pipelines;
using TVRoom.Broadcast;

namespace TVRoom.HLS
{
    public static class HlsTranscodeApiEndpoints
    {
        public static IEndpointRouteBuilder MapHlsTranscodeApiEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
        {
            app.MapPut("/hls/{transcodeId}/{*file}", async (string transcodeId, string file, PipeReader body, HlsTranscodeStore transcodeStore, ILogger logger) =>
            {
                if (!transcodeStore.TryGetTranscode(transcodeId, out var transcode))
                {
                    return Results.NotFound();
                }

                var extension = Path.GetExtension(file);
                if (IsInvalidFileName(file) || !(extension == ".ts" || extension == ".m3u8"))
                {
                    return Results.BadRequest();
                }

                await transcode.HlsLiveStream.IngestStreamFileAsync(file, body);

                return Results.NoContent();
            }).AllowAnonymous();

            return app;
        }

        private static bool IsInvalidFileName(string fileName) => fileName.AsSpan().ContainsAny(Path.GetInvalidFileNameChars());
    }
}
