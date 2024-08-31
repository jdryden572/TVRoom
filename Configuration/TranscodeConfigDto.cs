namespace TVRoom.Configuration
{
    public sealed record TranscodeConfigDto
    {
        public DateTime CreatedAt { get; set; }
        public required string Name { get; set; }
        public required string InputVideoParameters { get; set; }
        public required string OutputVideoParameters { get; set; }
    }
}
