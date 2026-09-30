namespace PropaneDriver.Shared.Constants
{
    // Role names issued as JWT role claims, one per role table (Drivers,
    // Supervisors, Administrators) the user has a row in.
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
