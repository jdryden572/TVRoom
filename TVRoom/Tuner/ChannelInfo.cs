using System.Text.Json.Serialization;

namespace TVRoom.Tuner
{
    public sealed record ChannelInfo(
        string GuideNumber, 
        string GuideName, 
        [property:JsonPropertyName("URL")] string Url);
}
