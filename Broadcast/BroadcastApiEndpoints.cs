using TVRoom.Helpers;

namespace TVRoom.Broadcast
{
    public static class BroadcastApiEndpoints
    {
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
                var filePath = Path.Combine(session.TranscodeDirectory.FullName, file);
                if (!(extension == ".ts" || extension == ".m3u8") || !File.Exists(filePath))
                {
                    return Results.NotFound();
                }

                string? contentType = extension == ".m3u8" ? "audio/mpegurl" : null;
                return Results.File(filePath, contentType);
            });

            getStream
                .RequireCors("AllowAll")
                .AllowAnonymous();

            return app;
        }
    }
}
