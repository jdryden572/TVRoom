using System.Text.Json.Serialization;

namespace TVRoom.Persistence
{
    [JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
    public enum UserRole
    {
        None = 0,
        Viewer,
        Administrator,
    }

    public class AuthorizedUser
    {
        public int Id { get; set; }
        public required string Email { get; set; }
        public UserRole Role { get; set; }
    }
}
