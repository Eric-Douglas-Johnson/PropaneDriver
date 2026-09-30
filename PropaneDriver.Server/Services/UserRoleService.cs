using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Constants;

namespace PropaneDriver.Server.Services
{
    // Translates between role names (UserRoles) and membership rows in the
    // per-role tables. Touches only UserId, never role-specific columns.
    // Changes are staged on the scoped DbContext; the caller saves.
    public class UserRoleService
    {
        private readonly PropaneDriverDbContext _db;
        private readonly DriverHistoryService _driverHistoryService;

        public UserRoleService(PropaneDriverDbContext db, DriverHistoryService driverHistoryService)
        {
            _db = db;
            _driverHistoryService = driverHistoryService;
        }

        // In UserRoles.All order.
        public async Task<List<string>> GetRolesAsync(Guid userId)
        {
            var roles = new List<string>();
            if (await _db.Drivers.AnyAsync(d => d.UserId == userId)) roles.Add(UserRoles.Driver);
            if (await _db.Supervisors.AnyAsync(s => s.UserId == userId)) roles.Add(UserRoles.Supervisor);
            if (await _db.Administrators.AnyAsync(a => a.UserId == userId)) roles.Add(UserRoles.Admin);
            return roles;
        }

        // Every user's roles in one pass, for account listings. Users with no roles are absent.
        public async Task<Dictionary<Guid, List<string>>> GetRolesByUserIdAsync()
        {
            var rolesByUserId = new Dictionary<Guid, List<string>>();

            void AddRole(IEnumerable<Guid> userIds, string role)
            {
                foreach (var userId in userIds)
                {
                    if (!rolesByUserId.TryGetValue(userId, out var roles))
                        rolesByUserId[userId] = roles = [];
                    roles.Add(role);
                }
            }

            AddRole(await _db.Drivers.Select(d => d.UserId).ToListAsync(), UserRoles.Driver);
            AddRole(await _db.Supervisors.Select(s => s.UserId).ToListAsync(), UserRoles.Supervisor);
            AddRole(await _db.Administrators.Select(a => a.UserId).ToListAsync(), UserRoles.Admin);
            return rolesByUserId;
        }

        // For a new account, which can't have any role rows yet.
        public void AddRoles(Guid userId, IEnumerable<string> roles)
        {
            foreach (var role in roles)
                AddRole(userId, role);
        }

        // Makes the user's role rows match the given roles. Returns the reason and stages
        // nothing when a role can't be removed.
        public async Task<string?> SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles)
        {
            var currentRoles = await GetRolesAsync(userId);

            var removingDriverRole = currentRoles.Contains(UserRoles.Driver) && !roles.Contains(UserRoles.Driver);
            if (removingDriverRole && await _driverHistoryService.HasHistoryAsync(userId))
                return "This user still has routes or fuel log entries, so they must keep the driver role.";

            foreach (var role in roles.Except(currentRoles))
                AddRole(userId, role);
            foreach (var role in currentRoles.Except(roles))
                await RemoveRoleAsync(userId, role);

            return null;
        }

        // For account deletion only: callers must clear each role's own data first.
        public async Task RemoveAllRolesAsync(Guid userId)
        {
            foreach (var role in await GetRolesAsync(userId))
                await RemoveRoleAsync(userId, role);
        }

        private void AddRole(Guid userId, string role)
        {
            switch (role)
            {
                case UserRoles.Driver: _db.Drivers.Add(new DriverDbRecord { UserId = userId }); break;
                case UserRoles.Supervisor: _db.Supervisors.Add(new SupervisorDbRecord { UserId = userId }); break;
                case UserRoles.Admin: _db.Administrators.Add(new AdministratorDbRecord { UserId = userId }); break;
                default: throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.");
            }
        }

        private async Task RemoveRoleAsync(Guid userId, string role)
        {
            switch (role)
            {
                case UserRoles.Driver: _db.Drivers.Remove((await _db.Drivers.FindAsync(userId))!); break;
                case UserRoles.Supervisor: _db.Supervisors.Remove((await _db.Supervisors.FindAsync(userId))!); break;
                case UserRoles.Admin: _db.Administrators.Remove((await _db.Administrators.FindAsync(userId))!); break;
                default: throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.");
            }
        }
    }
}
