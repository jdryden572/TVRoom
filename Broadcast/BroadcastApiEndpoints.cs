using Microsoft.AspNetCore.Builder;
using Microsoft.IO;
using System.IO.Pipelines;
using System.Text;
using TVRoom.Helpers;

namespace TVRoom.Broadcast
{
    public static class BroadcastApiEndpoints
    {
        private static RecyclableMemoryStreamManager _streamManager = new();

        public static IEndpointRouteBuilder MapBroadcastApiEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
        {
            var getStream = app.MapGet("/streams/{sessionId}/{*file}", (string sessionId, string file, BroadcastManager broadcastManager) =>
            {
                var session = broadcastManager.CurrentSession;
                if (session is null || session.BroadcastInfo.SessionId != sessionId)
                {
                    return Results.NotFound();
                }

                var extension = Path.GetExtension(file);
                if (!(extension == ".ts" || extension == ".m3u8") || !session.HlsLiveStream.TryGetFile(file, out var hlsStreamFile))
                {
                    return Results.NotFound();
                }

                return hlsStreamFile.GetResult();
            });

            getStream
                .RequireCors("AllowAll")
                .AllowAnonymous();

            app.MapPut("/streams/{sessionId}/{*file}", async (string sessionId, string file, PipeReader body, BroadcastManager broadcastManager, ILogger logger) =>
            {
                var session = broadcastManager.CurrentSession;
                if (session is null || session.BroadcastInfo.SessionId != sessionId)
                {
                    return Results.NotFound();
                }

                var extension = Path.GetExtension(file);
                if (IsInvalidFileName(file) || !(extension == ".ts" || extension == ".m3u8"))
                {
                    return Results.BadRequest();
                }

                await session.HlsLiveStream.IngestStreamFileAsync(file, body);

                return Results.NoContent();
            }).AllowAnonymous();

            return app;
        }

        private static bool IsInvalidFileName(string fileName) => fileName.AsSpan().ContainsAny(Path.GetInvalidFileNameChars());
    }
}
