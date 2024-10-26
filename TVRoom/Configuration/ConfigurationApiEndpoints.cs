namespace TVRoom.Configuration
{
    public static class ConfigurationApiEndpoints
    {
        public static IServiceCollection AddConfigurationServices(this IServiceCollection services)
        {
            services.AddSingleton<TranscodeConfigService>();
            return services;
        }

        public static IEndpointRouteBuilder MapConfigurationApiEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapPost("/transcodeConfig", async (TranscodeConfigDto config, TranscodeConfigService service) =>
            {
                await service.SaveNewConfig(config);
                return Results.Ok();
            });

            app.MapGet("/transcodeConfig", async (TranscodeConfigService service) =>
            {
                return await service.GetLatestConfig();
            });

            return app;
        }
    }
}
