using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace LivingRoom.Authorization
{
    public static class AuthorizationServiceRegistration
    {
        public static IServiceCollection AddGoogleAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(cookie =>
                {
                    cookie.LoginPath = "/login";
                    cookie.AccessDeniedPath = "/accessdenied";
                })
                .AddGoogle(google =>
                {
                    var section = configuration.GetRequiredSection("Authentication:Google");
                    google.ClientId = section["ClientId"] ?? throw new InvalidOperationException("Google client ID is required");
                    google.ClientSecret = section["ClientSecret"] ?? throw new InvalidOperationException("Google client secret is required");
                    google.Scope.Add("email");
                    google.Events.OnCreatingTicket = AuthenticationCallbacks.AddUserRolesAsync;
                    google.ClaimActions.MapJsonKey("picture", "picture", "url");

                    var onRedirectToAuth = google.Events.OnRedirectToAuthorizationEndpoint;
                    google.Events.OnRedirectToAuthorizationEndpoint = async ctx =>
                    {
                        if (ctx.Properties.GetParameter<bool>("Prompt"))
                        {
                            ctx.RedirectUri += "&prompt=consent";
                        }
                        await onRedirectToAuth(ctx);
                    };
                });

            return services;
        }

        public static IServiceCollection AddLivingRoomAuthorizationServices(this IServiceCollection services)
        {
            var viewerPolicy = new AuthorizationPolicyBuilder()
                .RequireRole(Roles.Viewer)
                .Build();

            var adminPolicy = new AuthorizationPolicyBuilder()
                .RequireRole(Roles.Administrator)
                .Build();

            services.AddAuthorizationBuilder()
                .AddPolicy(Policies.RequireViewer, viewerPolicy)
                .AddPolicy(Policies.RequireAdministrator, adminPolicy)
                .SetDefaultPolicy(viewerPolicy)
                .SetFallbackPolicy(viewerPolicy);

            return services.AddSingleton<UserManager>();
        }
    }
}
