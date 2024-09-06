using Microsoft.EntityFrameworkCore;
using TVRoom.Persistence;

namespace TVRoom.Authorization
{
    public class UserManager
    {
        private static string[] AdminRoles = [Roles.Administrator, Roles.Viewer];
        private static string[] ViewerRoles = [Roles.Viewer];

        private readonly string _administratorEmail;
        private readonly TVRoomContext _dbContext;

        public UserManager(IConfiguration configuration, TVRoomContext dbContext)
        {
            _administratorEmail = configuration["AdministratorEmail"] ?? throw new ArgumentException("AdministratorEmail not configured");
            _dbContext = dbContext;
        }

        public async Task<string[]> GetUserRolesAsync(string email)
        {
            if (_administratorEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
            {
                return AdminRoles;
            }

            var configuredRole = await _dbContext.AuthorizedUsers
                .Where(u => u.Email == email)
                .Select(u => u.Role)
                .FirstOrDefaultAsync();

            return configuredRole switch
            {
                UserRole.Administrator => AdminRoles,
                UserRole.Viewer => ViewerRoles,
                _ => Array.Empty<string>(),
            };
        }        
    }
}
