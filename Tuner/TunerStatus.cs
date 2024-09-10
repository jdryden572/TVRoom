namespace TVRoom.Tuner
{
    public record TunerStatus(
        string Resource,
        string TargetIP,
        string ChannelNumber,
        string ChannelName,
        int NetworkRate,
        int SignalStrengthPercent,
        int SignalQualityPercent,
        int SymbolQualityPercent)
    {
        public DateTime Timestamp { get; } = DateTime.UtcNow;
    }
}
