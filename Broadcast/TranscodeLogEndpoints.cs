using TVRoom.Authorization;

namespace TVRoom.Broadcast
{
    public static class TranscodeLogEndpoints
    {
        public static IEndpointRouteBuilder MapTranscodeLogEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/logs")
                .RequireAuthorization(Policies.RequireAdministrator);

            group.MapGet("/", (HlsConfiguration config) =>
            {
                var directory = config.BaseTranscodeDirectory;
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

                var directory = config.BaseTranscodeDirectory;
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

                var directory = config.BaseTranscodeDirectory;
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
