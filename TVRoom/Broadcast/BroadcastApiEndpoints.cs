using TVRoom.Authorization;

namespace TVRoom.Broadcast
{
    public static class BroadcastApiEndpoints
    {
        public static IEndpointRouteBuilder MapBroadcastApiEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/streams");

            var getMasterPlaylist = group.MapGet("/{sessionId}/master.m3u8", (string sessionId, BroadcastManager broadcastManager) =>
            {
                var session = broadcastManager.CurrentSession;
                if (session is null || session.BroadcastInfo.SessionId != sessionId)
                {
                    return Results.NotFound();
                }

                return session.HlsLiveStream.GetMasterPlaylist();
            });

            var getPlaylist = group.MapGet("/{sessionId}/live.m3u8", (string sessionId, BroadcastManager broadcastManager) =>
            {
                var session = broadcastManager.CurrentSession;
                if (session is null || session.BroadcastInfo.SessionId != sessionId)
                {
                    return Results.NotFound();
                }

                return session.HlsLiveStream.GetPlaylist();
            });

            var getStream = group.MapGet(@"/{sessionId}/live{index}.ts", (string sessionId, int index, BroadcastManager broadcastManager) =>
            {
                var session = broadcastManager.CurrentSession;
                if (session is null || session.BroadcastInfo.SessionId != sessionId)
                {
                    return Results.NotFound();
                }

                return session.HlsLiveStream.GetSegment(index); 
            });

            group
                .RequireCors("AllowAll")
                .AllowAnonymous();

            app.MapGet("/broadcast/current", (BroadcastManager broadcastManager, HttpRequest request) =>
            {
                var nowPlaying = broadcastManager.NowPlaying;
                if (nowPlaying is null)
                {
                    return Results.Ok(Array.Empty<string>());
                }
                return Results.Ok(new[] { $"{request.Scheme}://{request.Host}/streams/{nowPlaying.SessionId}/master.m3u8" });
            }).RequireAuthorization(Policies.RequireApiViewer);

            return app;
        }

        private static bool IsInvalidFileName(string fileName) => fileName.AsSpan().ContainsAny(Path.GetInvalidFileNameChars());
    }
}
