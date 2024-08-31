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
    }
}
