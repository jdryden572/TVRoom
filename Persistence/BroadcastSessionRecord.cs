namespace LivingRoom.Persistence
{
    public class BroadcastSessionRecord
    {
        public int Id { get; set; }
        public required string GuideNumber { get; set; }
        public required string GuideName { get; set; }
        public required string Url { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
    }
}
