using System.IO.Pipelines;
using TVRoom.Broadcast;

namespace TVRoom.Transcode
{
    public static class HlsTranscodeApiEndpoints
    {
        public static IEndpointRouteBuilder MapHlsTranscodeApiEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
        {
            app.MapPut("/hls/{transcodeId}/{*file}", async (string transcodeId, string file, PipeReader body, HlsTranscodeManager transcodeManager, ILogger logger) =>
            {
                if (!transcodeManager.TryGetTranscode(transcodeId, out var transcode))
                {
                    return Results.NotFound();
                }

                var extension = Path.GetExtension(file);
                if (IsInvalidFileName(file) || !(extension == ".ts" || extension == ".m3u8"))
                {
                    return Results.BadRequest();
                }

                var fileType = file switch
                {
                    "master.m3u8" => IngestFileType.MasterPlaylist,
                    "live.m3u8" => IngestFileType.Playlist,
                    _ => IngestFileType.Segment,
                };

                var payload = await body.PooledReadToEndAsync();
                var ingestFile = new IngestHlsFile(file, fileType, payload);

                await transcode.FileIngester.IngestStreamFileAsync(ingestFile);

                return Results.NoContent();
            }).AllowAnonymous();

            return app;
        }

        private static bool IsInvalidFileName(string fileName) => fileName.AsSpan().ContainsAny(Path.GetInvalidFileNameChars());
    }
}
