using TVRoom.Authorization;
using TVRoom.Configuration;

namespace TVRoom.Broadcast
{
    public static class BroadcastLogEndpoints
    {
        public static IEndpointRouteBuilder MapBroadcastLogEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/logs")
                .RequireAuthorization(Policies.RequireAdministrator);

            group.MapGet("/", (HlsConfiguration config) =>
            {
                var directory = config.LogDirectory;
                return directory.GetFiles("*.log")
                    .Select(f => new
                    {
                        f.Name,
                        f.Length,
                    });
            });

            group.MapGet("/{log}", (string log, HlsConfiguration config) =>
            {
                if (IsInvalidFileName(log))
                {
                    return Results.BadRequest();
                }

                var directory = config.LogDirectory;
                var file = new FileInfo(Path.Combine(directory.FullName, log));
                if (file.Exists)
                {
                    return Results.File(file.FullName);
                }

                return Results.NotFound();
            });

            group.MapDelete("/{log}", (string log, HlsConfiguration config) =>
            {
                if (IsInvalidFileName(log))
                {
                    return Results.BadRequest();
                }

                var directory = config.LogDirectory;
                var file = new FileInfo(Path.Combine(directory.FullName, log));
                if (file.Exists)
                {
                    file.Delete();
                    return Results.NoContent();
                }

                return Results.NotFound();
            });

            return app;
        }

        private static bool IsInvalidFileName(string fileName) => fileName.AsSpan().ContainsAny(Path.GetInvalidFileNameChars());
    }
}
