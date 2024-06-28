using LivingRoom.Tuner;

namespace LivingRoom.Broadcast
{
    public sealed record BroadcastInfo(ChannelInfo ChannelInfo, string SessionId, string FFmpegArguments)
    {
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    }
}
