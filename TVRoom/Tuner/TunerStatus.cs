using System.Text.Json.Serialization;

namespace TVRoom.Tuner
{
    public record TunerStatus(
        [property: JsonPropertyName("res")] string Resource,
        [property: JsonPropertyName("ip")] string? TargetIP,
        [property: JsonPropertyName("num")] string? ChannelNumber,
        [property: JsonPropertyName("name")] string? ChannelName,
        [property: JsonPropertyName("rate")] int? NetworkRate,
        [property: JsonPropertyName("sigS")] int? SignalStrengthPercent,
        [property: JsonPropertyName("sigQ")] int? SignalQualityPercent,
        [property: JsonPropertyName("symQ")] int? SymbolQualityPercent)
    {
        [JsonPropertyName("ts")]
        public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
