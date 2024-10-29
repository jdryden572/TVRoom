using TVRoom.Broadcast;
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

                var payload = await request.ReadToSharedBufferAsync($"master.m3u8_{DateTime.UtcNow:O}");
                await fileIngester.IngestStreamFileAsync(new IngestMasterPlaylist(payload));
                return Results.NoContent();
            });

            group.MapPut("/{transcodeId}/live.m3u8", async (string transcodeId, HttpRequest request, TranscodeSessionManager transcodeSessionManager) =>
            {
                if (!transcodeSessionManager.TryGetFileIngester(transcodeId, out var fileIngester))
                {
                    return Results.NotFound();
                }

                var payload = await request.ReadToSharedBufferAsync($"live.m3u8_{DateTime.UtcNow:O}");
                await fileIngester.IngestStreamFileAsync(new IngestStreamPlaylist(payload));
                return Results.NoContent();
            });

            group.MapPut(@"/{transcodeId}/{segment:regex(live\d+.ts)}", async (string transcodeId, string segment, HttpRequest request, TranscodeSessionManager transcodeSessionManager) =>
            {
                if (!transcodeSessionManager.TryGetFileIngester(transcodeId, out var fileIngester))
                {
                    return Results.NotFound();
                }

                var payload = await request.ReadToSharedBufferAsync($"{segment}_{DateTime.UtcNow:O}");
                await fileIngester.IngestStreamFileAsync(new IngestStreamSegment(segment, payload));
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
