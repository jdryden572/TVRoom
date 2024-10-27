using TVRoom.Configuration;

namespace TVRoom.Transcode
{
    public static class TranscodeServiceRegistration
    {
        public static IServiceCollection AddTVRoomHlsServices(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddSingleton<TranscodeSessionManager>();
        }
    }
}
