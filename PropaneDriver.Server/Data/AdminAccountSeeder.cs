using Microsoft.EntityFrameworkCore;

namespace PropaneDriver.Server.Data
{
    // Seeds (or updates) the admin DriverDbRecord on app startup based on the
    // "AdminSeed" config block. Idempotent — re-running the app won't create
    // duplicates and won't overwrite a manually-rotated password (we only set
    // the password if the row is being created for the first time).
    //
    // Also force-resets the legacy "test_driver" account back to a plain
    // "driver" role so it can no longer reach admin-gated endpoints, even if
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
                        "Set the value in appsettings (or User Secrets) and restart to seed the admin user.");
                }
                else
                {
                    var existingAdmin = db.Drivers.FirstOrDefault(d => d.UserName == adminUserName);
                    if (existingAdmin is null)
                    {
                        var newAdmin = new DriverDbRecord
                        {
                            Id = Guid.NewGuid(),
                            UserName = adminUserName,
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                            Role = "admin",
                            FirstName = adminFirstName,
                            MiddleName = string.Empty,
                            LastName = adminLastName,
                            Email = adminEmail,
                            PhoneNumber = string.Empty,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.Drivers.Add(newAdmin);
                        db.SaveChanges();
                        logger.LogInformation(
                            "Seeded admin account '{UserName}'. Update the password via the password-reset flow.",
                            adminUserName);
                    }
                    else if (existingAdmin.Role != "admin")
                    {
                        // Pre-existing row with the same UserName but the wrong role —
                        // promote it. Don't touch the stored password.
                        existingAdmin.Role = "admin";
                        db.SaveChanges();
                        logger.LogInformation(
                            "Promoted existing account '{UserName}' to admin role.",
                            adminUserName);
                    }
                }

                // Belt-and-suspenders demotion of the seed test account so it
                // can never reach admin-gated endpoints regardless of prior
                // hand-edits to the Drivers table.
                var legacyTestDriver = db.Drivers.FirstOrDefault(d => d.UserName == "test_driver");
                if (legacyTestDriver is not null && legacyTestDriver.Role != "driver")
                {
                    legacyTestDriver.Role = "driver";
                    db.SaveChanges();
                    logger.LogInformation("Reset 'test_driver' role back to 'driver'.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Admin account seed failed; the server will keep running, but admin login may not work until the issue is resolved.");
            }
        }
    }
}
