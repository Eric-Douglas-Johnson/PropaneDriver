using Microsoft.EntityFrameworkCore;

namespace PropaneDriver.Server.Data
{
    // Seeds (or updates) the admin account on app startup based on the
    // "AdminSeed" config block. Idempotent — re-running the app won't create
    // duplicates and won't overwrite a manually-rotated password (we only set
    // the password if the row is being created for the first time).
    //
    // Also force-resets the legacy "test_driver" account back to the driver
    // role alone so it can no longer reach admin-gated endpoints, even if
    // an earlier deployment had elevated it.
    public static class AdminAccountSeeder
    {
        public static void EnsureAdminSeeded(IServiceProvider services, ILogger logger)
        {
            using var scope = services.CreateScope();
            try
            {
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

                var adminUserName = configuration["AdminSeed:UserName"] ?? "admin";
                var adminPassword = configuration["AdminSeed:Password"];
                var adminEmail = configuration["AdminSeed:Email"] ?? string.Empty;
                var adminFirstName = configuration["AdminSeed:FirstName"] ?? "Site";
                var adminLastName = configuration["AdminSeed:LastName"] ?? "Admin";

                if (string.IsNullOrWhiteSpace(adminPassword))
                {
                    logger.LogWarning(
                        "AdminSeed:Password is not configured; skipping admin account bootstrap. " +
                        "Set the value in local.settings.json (or an AdminSeed__Password application setting) and restart to seed the admin user.");
                }
                else
                {
                    var existingAdmin = db.Users.FirstOrDefault(u => u.UserName == adminUserName);
                    if (existingAdmin is null)
                    {
                        var newAdmin = new UserDbRecord
                        {
                            Id = Guid.NewGuid(),
                            UserName = adminUserName,
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                            FirstName = adminFirstName,
                            MiddleName = string.Empty,
                            LastName = adminLastName,
                            Email = adminEmail,
                            PhoneNumber = string.Empty,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.Users.Add(newAdmin);
                        db.Administrators.Add(new AdministratorDbRecord { UserId = newAdmin.Id });
                        db.SaveChanges();
                        logger.LogInformation(
                            "Seeded admin account '{UserName}'. Update the password via the password-reset flow.",
                            adminUserName);
                    }
                    else if (!db.Administrators.Any(a => a.UserId == existingAdmin.Id))
                    {
                        // Pre-existing account with the same UserName but no admin role —
                        // grant it, keeping any other roles. Don't touch the stored password.
                        db.Administrators.Add(new AdministratorDbRecord { UserId = existingAdmin.Id });
                        db.SaveChanges();
                        logger.LogInformation(
                            "Granted the admin role to existing account '{UserName}'.",
                            adminUserName);
                    }
                }

                // Belt-and-suspenders demotion of the seed test account so it
                // can never reach admin-gated endpoints regardless of prior
                // hand-edits to the role tables.
                var legacyTestUser = db.Users.FirstOrDefault(u => u.UserName == "test_driver");
                if (legacyTestUser is not null)
                {
                    var elevatedRoleRows = db.Administrators.Count(a => a.UserId == legacyTestUser.Id)
                        + db.Supervisors.Count(s => s.UserId == legacyTestUser.Id);
                    var hasDriverRole = db.Drivers.Any(d => d.UserId == legacyTestUser.Id);

                    if (elevatedRoleRows > 0 || !hasDriverRole)
                    {
                        db.Administrators.RemoveRange(db.Administrators.Where(a => a.UserId == legacyTestUser.Id));
                        db.Supervisors.RemoveRange(db.Supervisors.Where(s => s.UserId == legacyTestUser.Id));
                        if (!hasDriverRole)
                            db.Drivers.Add(new DriverDbRecord { UserId = legacyTestUser.Id });
                        db.SaveChanges();
                        logger.LogInformation("Reset 'test_driver' to the driver role only.");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Admin account seed failed; the server will keep running, but admin login may not work until the issue is resolved.");
            }
        }
    }
}
