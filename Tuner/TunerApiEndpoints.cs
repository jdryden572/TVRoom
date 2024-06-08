using LivingRoom.Authorization;
using LivingRoom.Helpers;

namespace LivingRoom.Tuner
{
    public sealed record ChannelInfo(string GuideNumber, string GuideName, string Url);

    public static class TunerApiEndpoints
    {
        private const string TunerClient = "TunerClient";

        public static IServiceCollection AddTunerServices(this IServiceCollection services, IConfiguration configuration)
        {
            var tunerAddress = configuration.GetRequiredValue("TunerAddress");
            services.AddHttpClient(TunerClient, 
                client => client.BaseAddress = new Uri(tunerAddress));

            return services;
        }

        public static IEndpointRouteBuilder MapTunerApiEndpoints(this IEndpointRouteBuilder app)
        {
            var getChannels = app.MapGet("/channels", async (IHttpClientFactory clientFactory) =>
            {
                var client = clientFactory.CreateClient(TunerClient);
                using var response = await client.GetAsync("lineup.json");
                response.EnsureSuccessStatusCode();
                var channels = await response.Content.ReadFromJsonAsync<ChannelInfo[]>();
                return Results.Ok(channels);
            });
                
            getChannels.RequireAuthorization(Policies.RequireAdministrator);
            return app;
        }
    }
}
