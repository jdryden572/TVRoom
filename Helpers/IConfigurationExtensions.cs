namespace TVRoom.Helpers
{
    internal static class IConfigurationExtensions
    {
        public static string GetRequiredValue(this IConfiguration configuration, string key)
        {
            return configuration[key] ?? throw new ArgumentNullException($"Configuration key '{key}' not set");
        }
    }
}
