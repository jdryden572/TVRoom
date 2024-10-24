namespace TVRoom.Persistence
{
    public class TranscodeConfig
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public required string InputVideoParameters { get; set; }
        public required string OutputVideoParameters { get; set; }
    }
}
