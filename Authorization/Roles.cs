namespace TVRoom.Authorization
{
    public static class Roles
    {
        public const string Administrator = "Admin";
        public const string Viewer = "Viewer";
    }

    public static class Policies
    {
        public const string RequireAdministrator = "RequireAdministrator";
        public const string RequireViewer = "RequireViewer";
    }
}
