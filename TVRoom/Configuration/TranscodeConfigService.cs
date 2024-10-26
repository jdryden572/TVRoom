using TVRoom.Persistence;
using Microsoft.EntityFrameworkCore;

namespace TVRoom.Configuration
{
    public sealed class TranscodeConfigService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public TranscodeConfigService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }


        public async Task<TranscodeConfigDto> GetLatestConfig()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TVRoomContext>();
            var config = await context.TranscodeConfigs.OrderByDescending(c => c.Id).FirstOrDefaultAsync();
            if (config is null)
            {
                return new TranscodeConfigDto
                {
                    Name = string.Empty,
                    InputVideoParameters = string.Empty,
                    OutputVideoParameters = string.Empty,
                };
            }

            return new TranscodeConfigDto
            {
                Name = config.Name,
                InputVideoParameters = config.InputVideoParameters,
                OutputVideoParameters = config.OutputVideoParameters,
            };
        }

        public async Task SaveNewConfig(TranscodeConfigDto config)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TVRoomContext>();
            var persistedConfig = new TranscodeConfig
            {
                CreatedAt = DateTime.UtcNow,
                Name = config.Name,
                InputVideoParameters = config.InputVideoParameters,
                OutputVideoParameters = config.OutputVideoParameters,
            };
            context.TranscodeConfigs.Add(persistedConfig);
            await context.SaveChangesAsync();
        }
    }
}
