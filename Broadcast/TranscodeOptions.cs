namespace TVRoom.Broadcast
{
    public sealed record TranscodeOptions
    {
        public required int BitRateKbps { get; init; }
        public string InputVideoOptions { get; init; } = string.Empty;
        public string OutputVideoOptions { get; init; } = string.Empty;
    }

    
}
