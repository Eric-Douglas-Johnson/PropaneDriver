namespace PropaneDriver.Shared.Constants
{
    // Role names stored in Drivers.Role and issued as the JWT role claim.
    public static class UserRoles
    {
        public const string Driver = "driver";
        public const string Supervisor = "supervisor";
        public const string Admin = "admin";

        // Comma-separated means "any of" to [Authorize(Roles = ...)] and AuthorizeView.
        public const string SupervisorOrAdmin = Supervisor + "," + Admin;

        public static readonly IReadOnlyList<string> All = [Driver, Supervisor, Admin];
    }
}
