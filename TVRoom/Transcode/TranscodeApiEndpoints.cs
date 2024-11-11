using TVRoom.HLS;

namespace TVRoom.Transcode
{
    public static class TranscodeApiEndpoints
    {
        public static IEndpointRouteBuilder MapTranscodeApiEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/transcode");

            group.MapPut("/{transcodeId}/master.m3u8", async (string transcodeId, HttpRequest request, TranscodeSessionManager transcodeSessionManager) =>
            {
                if (!transcodeSessionManager.TryGetFileIngester(transcodeId, out var fileIngester))
                {
                    return Results.NotFound();
                }

                await fileIngester.IngestMasterPlaylist(request);
                return Results.NoContent();
            });

            group.MapPut("/{transcodeId}/live.m3u8", async (string transcodeId, HttpRequest request, TranscodeSessionManager transcodeSessionManager) =>
            {
                if (!transcodeSessionManager.TryGetFileIngester(transcodeId, out var fileIngester))
                {
                    return Results.NotFound();
                }

                await fileIngester.IngestStreamPlaylist(request);
                return Results.NoContent();
            });

            group.MapPut(@"/{transcodeId}/{segment:regex(live\d+.ts)}", async (string transcodeId, string segment, HttpRequest request, TranscodeSessionManager transcodeSessionManager) =>
            {
                if (!transcodeSessionManager.TryGetFileIngester(transcodeId, out var fileIngester))
                {
                    return Results.NotFound();
                }

                await fileIngester.IngestStreamSegment(segment, request);
                return Results.NoContent();
            });

            group.AllowAnonymous();

            group.MapGet("/bufferstats", () => new
            {
                RentedBufferCount = SharedBuffer.RentedBufferCount.Value,
                RentedBytes = SharedBuffer.RentedBytes.Value,
            });

            return app;
        }
    }
}
