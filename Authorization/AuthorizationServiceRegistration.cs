using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace LivingRoom.Authorization
{
    public static class AuthorizationServiceRegistration
    {
        public static IServiceCollection RegisterGoogleAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
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
                });

            return services;
        }

        public static IServiceCollection RegisterLivingRoomAuthorizationServices(this IServiceCollection services)
        {
            var viewerPolicy = new AuthorizationPolicyBuilder()
                .RequireRole(Roles.Viewer)
                .Build();

            services.AddAuthorizationBuilder()
                .AddPolicy("RequireViewer", viewerPolicy)
                .SetDefaultPolicy(viewerPolicy)
                .SetFallbackPolicy(viewerPolicy);

            return services.AddSingleton<UserManager>();
        }
    }
}
