using Microsoft.AspNetCore.Authentication.OAuth;
using System.Security.Claims;

namespace TVRoom.Authorization
{
    public static class AuthenticationCallbacks
    {
        public static Task AddUserRolesAsync(OAuthCreatingTicketContext context)
        {
            if (context.Principal is not ClaimsPrincipal principal
                || principal.FindFirst(ClaimTypes.Email) is not Claim emailClaim)
            {
                return Task.CompletedTask;
            }

            var userService = context.HttpContext.RequestServices.GetRequiredService<UserManager>();

            var newIdentity = new ClaimsIdentity(principal.Identity);
            foreach (var role in userService.GetUserRoles(emailClaim.Value))
            {
                newIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
            }

            context.Principal = new ClaimsPrincipal(newIdentity);

            return Task.CompletedTask;
        }
    }
}
