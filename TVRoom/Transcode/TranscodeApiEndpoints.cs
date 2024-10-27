using System.IO.Pipelines;
using TVRoom.Broadcast;
using TVRoom.HLS;

namespace TVRoom.Transcode
{
    public static class TranscodeApiEndpoints
    {
        public static IEndpointRouteBuilder MapTranscodeApiEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/transcode");

            group.MapPut("/{transcodeId}/master.m3u8", async (string transcodeId, PipeReader body, TranscodeSessionManager transcodeSessionManager) =>
            {
                if (!transcodeSessionManager.TryGetFileIngester(transcodeId, out var fileIngester))
                {
                    return Results.NotFound();
                }

                var payload = await body.ReadToSharedBufferAsync();
                await fileIngester.IngestStreamFileAsync(new IngestMasterPlaylist(payload));
                return Results.NoContent();
            });

            group.MapPut("/{transcodeId}/live.m3u8", async (string transcodeId, PipeReader body, TranscodeSessionManager transcodeSessionManager) =>
            {
                if (!transcodeSessionManager.TryGetFileIngester(transcodeId, out var fileIngester))
                {
                    return Results.NotFound();
                }

                var payload = await body.ReadToSharedBufferAsync();
                await fileIngester.IngestStreamFileAsync(new IngestStreamPlaylist(payload));
                return Results.NoContent();
            });

            group.MapPut(@"/{transcodeId}/{segment:regex(live\d+.ts)}", async (string transcodeId, string segment, PipeReader body, TranscodeSessionManager transcodeSessionManager) =>
            {
                if (!transcodeSessionManager.TryGetFileIngester(transcodeId, out var fileIngester))
                {
                    return Results.NotFound();
                }

                var payload = await body.ReadToSharedBufferAsync();
                await fileIngester.IngestStreamFileAsync(new IngestStreamSegment(segment, payload));
                return Results.NoContent();
            });

            group.AllowAnonymous();

            group.MapGet("/bufferstats", () => new
            {
                SharedBuffer.RentedBufferCount,
                SharedBuffer.RentedBytes,
            });

            return app;
        }
    }
}
