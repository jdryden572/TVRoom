using LivingRoom.Persistence;
using LivingRoom.Tuner;
using Microsoft.EntityFrameworkCore;

namespace LivingRoom.Broadcast
{
    public sealed class BroadcastHistoryService
    {
        private readonly TVRoomContext _context;

        public BroadcastHistoryService(TVRoomContext context) => _context = context;

        public async Task StartNewBroadcast(ChannelInfo channel)
        {
            _context.BroadcastSessionRecords.Add(new BroadcastSessionRecord
            {
                GuideNumber = channel.GuideNumber,
                GuideName = channel.GuideName,
                Url = channel.Url,
                StartedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();
        }

        public async Task<BroadcastSessionRecord?> GetLatestBroadcast()
        {
            return await _context.BroadcastSessionRecords
                .AsNoTracking()
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();
        }

        public async Task EndCurrentBroadcast()
        {
            var latest = await _context.BroadcastSessionRecords
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            if (latest != null && latest.EndedAt is null)
            {
                latest.EndedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
    }
}
