﻿namespace TVRoom.HLS
{
    public static class HlsServiceRegistration
    {
        public static IServiceCollection AddTVRoomHlsServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<HlsTranscodeOptions>(configuration.GetSection(HlsTranscodeOptions.SectionName));

            return services
                .AddSingleton<HlsConfiguration>()
                .AddSingleton<HlsTranscodeStore>()
                .AddScoped<HlsTranscodeFactory>();
        }
    }
}