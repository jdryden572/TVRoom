namespace LivingRoom.Authorization
{

    public class UserManager
    {
        private readonly string _administratorEmail;

        public UserManager(IConfiguration configuration)
        {
            _administratorEmail = configuration["AdministratorEmail"] ?? throw new ArgumentException("AdministratorEmail not configured");
        }

        public string[] GetUserRoles(string email)
        {
            if (_administratorEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
            {
                return [ Roles.Administrator, Roles.Viewer ];
            }

            return Array.Empty<string>();
        }        
    }
}
