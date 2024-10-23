namespace TVRoom.Broadcast
{
    public static class BroadcastServiceRegistration
    {
        public static IServiceCollection AddTVRoomBroadcastServices(this IServiceCollection services)
        {
            return services
                .AddScoped<BroadcastSessionFactory>()
                .AddScoped<BroadcastHistoryService>()
                .AddSingleton<BroadcastManager>();
        }
    }
}
