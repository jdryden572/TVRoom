using Microsoft.EntityFrameworkCore;
using TVRoom.Persistence;

namespace TVRoom.Authorization
{
    public static class UserApiEndpoints
    {
        public static IEndpointRouteBuilder MapUserApiEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/users")
                .RequireAuthorization(Policies.RequireAdministrator);

            group.MapGet("/", async (TVRoomContext dbContext) =>
            {
                return await dbContext.AuthorizedUsers.AsNoTracking().ToArrayAsync();
            });

            group.MapPost("/", async (AuthorizedUserDto newUser, TVRoomContext dbContext) =>
            {
                dbContext.AuthorizedUsers.Add(new AuthorizedUser
                {
                    Email = newUser.Email,
                    Role = newUser.Role,
                });
                await dbContext.SaveChangesAsync();
                return Results.NoContent();
            });

            group.MapDelete("/{id}", async (int id, TVRoomContext dbContext) =>
            {
                var user = await dbContext.AuthorizedUsers.FindAsync(id);
                if (user is null)
                {
                    return Results.NotFound();
                }

                dbContext.AuthorizedUsers.Remove(user);
                await dbContext.SaveChangesAsync();
                return Results.NoContent();
            });

            return app;
        }
    }

    public record AuthorizedUserDto(string Email, UserRole Role);
}
