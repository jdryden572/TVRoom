using Microsoft.AspNetCore.Authentication.OAuth;
using System.Security.Claims;

namespace TVRoom.Authorization
{
    public static class AuthenticationCallbacks
    {
        public static async Task AddUserRolesAsync(OAuthCreatingTicketContext context)
        {
            if (context.Principal is not ClaimsPrincipal principal
                || principal.FindFirst(ClaimTypes.Email) is not Claim emailClaim)
            {
                return;
            }

            var userService = context.HttpContext.RequestServices.GetRequiredService<UserManager>();

            var newIdentity = new ClaimsIdentity(principal.Identity);
            foreach (var role in await userService.GetUserRolesAsync(emailClaim.Value))
            {
                newIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
            }

            context.Principal = new ClaimsPrincipal(newIdentity);
        }
    }
}
