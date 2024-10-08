﻿namespace TVRoom.Broadcast
{
    public static class BroadcastServiceRegistration
    {
        public static IServiceCollection AddTVRoomBroadcastServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<HlsTranscodeOptions>(configuration.GetSection(HlsTranscodeOptions.SectionName));

            return services
                .AddScoped<BroadcastSessionFactory>()
                .AddScoped<BroadcastHistoryService>()
                .AddSingleton<HlsConfiguration>()
                .AddSingleton<BroadcastManager>();
        }
    }
}
