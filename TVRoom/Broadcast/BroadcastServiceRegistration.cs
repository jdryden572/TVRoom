﻿namespace TVRoom.Broadcast
{
    public static class BroadcastServiceRegistration
    {
        public static IServiceCollection AddTVRoomBroadcastServices(this IServiceCollection services)
        {
            return services
                .AddSingleton<BroadcastSessionFactory>()
                .AddSingleton<BroadcastHistoryService>()
                .AddSingleton<BroadcastManager>();
        }
    }
}
