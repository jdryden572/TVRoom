using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using System.Security.Claims;

namespace TVRoom.Authorization
{
    public static class SignInEndpoints
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "Logging performance not important for errors here")]
        public static IEndpointRouteBuilder MapSignInEndpoints(this IEndpointRouteBuilder app)
        {
            var validationSettings = CreateTokenValidationSettings(app.ServiceProvider.GetRequiredService<IConfiguration>());

            app.MapPost("/signin", async (SignInPostBody body, HttpContext context, ILogger<Program> logger) =>
            {
                GoogleJsonWebSignature.Payload? verifiedTokenPayload = null;
                try
                {
                    var payload = await GoogleJsonWebSignature.ValidateAsync(body.IdToken, validationSettings);
                    if (payload.EmailVerified == true)
                    {
                        verifiedTokenPayload = payload;
                    }
                }
                catch (InvalidJwtException ex)
                {
                    logger.LogError(ex, "Invalid ID token provided for sign-in.");
                }

                if (verifiedTokenPayload is null)
                {
                    return Results.BadRequest();
                }

                var userManager = context.RequestServices.GetRequiredService<UserManager>();
                var roles = await userManager.GetUserRolesAsync(verifiedTokenPayload.Email);
                if (roles.Length == 0)
                {
                    return Results.Forbid();
                }

                var claimsIdentity = new ClaimsIdentity(BearerTokenDefaults.AuthenticationScheme);
                claimsIdentity.AddClaims([
                    new Claim(ClaimTypes.Email, verifiedTokenPayload.Email),
                    new Claim(ClaimTypes.Name, verifiedTokenPayload.Name ?? string.Empty)
                ]);
                claimsIdentity.AddClaims(roles.Select(role => new Claim(ClaimTypes.Role, role)));

                await context.SignInAsync(BearerTokenDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));
                return Results.Empty;
            }).AllowAnonymous();

            return app;
        }

        /// <summary>
        /// Restricts accepted ID tokens to those issued for our own Google OAuth clients. Without an
        /// explicit audience, Google.Apis.Auth suppresses audience validation altogether and any valid
        /// Google-signed token would be accepted, whichever application it had been issued to.
        /// </summary>
        private static GoogleJsonWebSignature.ValidationSettings CreateTokenValidationSettings(IConfiguration configuration)
        {
            var section = configuration.GetRequiredSection("Authentication:Google");
            var audiences = new List<string>
            {
                section["ClientId"] ?? throw new InvalidOperationException("Google client ID is required"),
            };

            // Non-web callers (native apps) sign in with their own OAuth client, so their
            // client IDs have to be trusted here too.
            audiences.AddRange(section.GetSection("ApiClientIds").Get<string[]>() ?? []);

            return new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = audiences,
            };
        }

        private sealed record SignInPostBody(string IdToken);
    }
}
