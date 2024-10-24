using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace TVRoom.Persistence
{
    public class TVRoomContext : DbContext, IDataProtectionKeyContext
    {
        public TVRoomContext(DbContextOptions<TVRoomContext> options)
            : base(options)
        {
        }

        public DbSet<TranscodeConfig> TranscodeConfigs { get; set; }

        public DbSet<BroadcastSessionRecord> BroadcastSessionRecords { get; set; }

        public DbSet<AuthorizedUser> AuthorizedUsers { get; set; }

        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    }
}
