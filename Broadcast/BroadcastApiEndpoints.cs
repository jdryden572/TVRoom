using System.IO.Pipelines;

namespace TVRoom.Broadcast
{
    public static class BroadcastApiEndpoints
    {
        public static IEndpointRouteBuilder MapBroadcastApiEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
        {
            var group = app.MapGroup("/streams");

            var getMasterPlaylist = group.MapGet("/{sessionId}/master.m3u8", (string sessionId, BroadcastManager broadcastManager) =>
            {
                var session = broadcastManager.CurrentSession;
                if (session is null || session.BroadcastInfo.SessionId != sessionId)
                {
                    return Results.NotFound();
                }

                return session.HlsLiveStream.GetMasterPlaylist() ?? Results.NotFound();
            });

            var getPlaylist = group.MapGet("/{sessionId}/live.m3u8", (string sessionId, BroadcastManager broadcastManager) =>
            {
                var session = broadcastManager.CurrentSession;
                if (session is null || session.BroadcastInfo.SessionId != sessionId)
                {
                    return Results.NotFound();
                }

                return session.HlsLiveStream.GetPlaylist() ?? Results.NotFound();
            });

            var getStream = group.MapGet(@"/{sessionId}/{segment:regex(^live\d+\.ts$)}", (string sessionId, string segment, BroadcastManager broadcastManager) =>
            {
                var session = broadcastManager.CurrentSession;
                if (session is null || session.BroadcastInfo.SessionId != sessionId)
                {
                    return Results.NotFound();
                }

                return session.HlsLiveStream.GetSegment(segment) ?? Results.NotFound(); 
            });

            group
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
