using LivingRoom.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LivingRoom.Configuration
{
    public sealed class TranscodeConfigService
    {
        private readonly TVRoomContext _context;

        public TranscodeConfigService(TVRoomContext context) => _context = context;

        public async Task<TranscodeConfigDto> GetLatestConfig()
        {
            var config = await _context.TranscodeConfigs.OrderByDescending(c => c.Id).FirstOrDefaultAsync();
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
            var persistedConfig = new TranscodeConfig
            {
                CreatedAt = DateTime.UtcNow,
                Name = config.Name,
                InputVideoParameters = config.InputVideoParameters,
                OutputVideoParameters = config.OutputVideoParameters,
            };
            _context.TranscodeConfigs.Add(persistedConfig);
            await _context.SaveChangesAsync();
        }
    }
}
