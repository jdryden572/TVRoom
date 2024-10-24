using TVRoom.Authorization;
using TVRoom.Helpers;

namespace TVRoom.Tuner
{
    public static class TunerApiEndpoints
    {
        public static IServiceCollection AddTunerServices(this IServiceCollection services, IConfiguration configuration)
        {
            var tunerAddress = configuration.GetRequiredValue("TunerAddress");
            services.AddHttpClient(TunerClient.TunerHttpClientName, 
                client => client.BaseAddress = new Uri(tunerAddress));
            services.AddSingleton<TunerClient>();
            services.AddSingleton<TunerStatusProvider>();

            return services;
        }

        public static IEndpointRouteBuilder MapTunerApiEndpoints(this IEndpointRouteBuilder app)
        {
            var getChannels = app.MapGet("/channels", async (TunerClient tunerClient) =>
            {
                var channels = await tunerClient.GetAllChannelsAsync();
                return Results.Ok(channels);
            });
                
            getChannels.RequireAuthorization(Policies.RequireAdministrator);
            return app;
        }
    }
}
