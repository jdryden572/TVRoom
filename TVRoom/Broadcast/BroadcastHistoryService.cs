using TVRoom.Persistence;
using TVRoom.Tuner;
using Microsoft.EntityFrameworkCore;

namespace TVRoom.Broadcast
{
    public sealed class BroadcastHistoryService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public BroadcastHistoryService(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task StartNewBroadcast(ChannelInfo channel)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TVRoomContext>();
            context.BroadcastSessionRecords.Add(new BroadcastSessionRecord
            {
                GuideNumber = channel.GuideNumber,
                GuideName = channel.GuideName,
                Url = channel.Url,
                StartedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        public async Task<BroadcastSessionRecord?> GetLatestBroadcast()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TVRoomContext>();
            return await context.BroadcastSessionRecords
                .AsNoTracking()
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();
        }

        public async Task EndCurrentBroadcast()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TVRoomContext>();
            var latest = await context.BroadcastSessionRecords
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            if (latest != null && latest.EndedAt is null)
            {
                latest.EndedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
        }
    }
}
