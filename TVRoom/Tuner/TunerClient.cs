using System.Text.Json.Serialization;

namespace TVRoom.Tuner
{
    public sealed class TunerClient
    {
        public const string TunerHttpClientName = "TunerClient";

        private readonly IHttpClientFactory _httpClientFactory;

        public TunerClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ChannelInfo[]> GetAllChannelsAsync()
        {
            var client = _httpClientFactory.CreateClient(TunerHttpClientName);
            using var response = await client.GetAsync("lineup.json");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ChannelInfo[]>() ?? Array.Empty<ChannelInfo>();
        }

        public async Task<ChannelInfo?> GetChannelAsync(string guideNumber)
        {
            var allChannels = await GetAllChannelsAsync();
            return allChannels.FirstOrDefault(c => c.GuideNumber == guideNumber);
        }

        public async Task<TunerStatus[]> GetTunerStatusesAsync(CancellationToken cancellation)
        {
            var client = _httpClientFactory.CreateClient(TunerHttpClientName);
            using var response = await client.GetAsync("status.json", cancellation);
            response.EnsureSuccessStatusCode();
            var internalStatuses = await response.Content.ReadFromJsonAsync<TunerStatusInternal[]>(cancellation) ?? Array.Empty<TunerStatusInternal>();
            return internalStatuses.Select(s => s.ToTunerStatus()).ToArray();
        }

        public sealed record TunerStatusInternal(
            string Resource,
            string TargetIP,
            [property: JsonPropertyName("VctNumber")] string ChannelNumber,
            [property: JsonPropertyName("VctName")] string ChannelName,
            int NetworkRate,
            int SignalStrengthPercent,
            int SignalQualityPercent,
            int SymbolQualityPercent)
        {
            public TunerStatus ToTunerStatus() => 
                new TunerStatus(
                    Resource,
                    TargetIP,
                    ChannelNumber,
                    ChannelName,
                    NetworkRate,
                    SignalStrengthPercent,
                    SignalQualityPercent,
                    SymbolQualityPercent);
        }
    }
}
