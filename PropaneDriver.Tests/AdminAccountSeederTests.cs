using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Tests;

// AdminAccountSeeder runs every app startup, so its idempotency is what
// keeps deployments from accidentally trampling a hand-rotated admin
// password. These tests pin that contract.
public class AdminAccountSeederTests
{
    private static IServiceProvider BuildServices(
        Action<DbContextOptionsBuilder> dbBuilderConfigurator,
        IDictionary<string, string?> adminSeedConfig)
    {
        var serviceCollection = new ServiceCollection();

        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(adminSeedConfig)
            .Build();
        serviceCollection.AddSingleton<IConfiguration>(configurationRoot);

        serviceCollection.AddDbContext<PropaneDriverDbContext>(dbBuilderConfigurator);

        return serviceCollection.BuildServiceProvider();
    }

    private static IServiceProvider BuildServicesWithSharedDb(
        string databaseName,
        IDictionary<string, string?> adminSeedConfig)
        => BuildServices(
            options => options.UseInMemoryDatabase(databaseName),
            adminSeedConfig);

    private static IDictionary<string, string?> AdminSeedConfig(
        string userName = "admin",
        string? password = "BootstrapPw123!",
        string email = "admin@test.local",
        string firstName = "Site",
        string lastName = "Admin")
        => new Dictionary<string, string?>
        {
            ["AdminSeed:UserName"] = userName,
            ["AdminSeed:Password"] = password,
            ["AdminSeed:Email"] = email,
            ["AdminSeed:FirstName"] = firstName,
            ["AdminSeed:LastName"] = lastName,
        };

    [Fact]
    public void EnsureAdminSeeded_NoExistingRow_CreatesAdminWithHashedPassword()
    {
        var dbName = $"seeder-create-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig(password: "Initial!Pw"));

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var seeded = db.Drivers.Single(d => d.UserName == "admin");

        Assert.Equal("admin", seeded.Role);
        Assert.NotEqual("Initial!Pw", seeded.PasswordHash); // hashed, not plaintext
        Assert.True(BCrypt.Net.BCrypt.Verify("Initial!Pw", seeded.PasswordHash));
        Assert.Equal("admin@test.local", seeded.Email);
        Assert.Equal("Site", seeded.FirstName);
        Assert.Equal("Admin", seeded.LastName);
    }

    [Fact]
    public void EnsureAdminSeeded_ExistingAdmin_DoesNotOverwritePassword()
    {
        // The whole point of this test: a deploy that re-runs the seeder
        // with the original (or stale) AdminSeed:Password value must not
        // overwrite a password the operator has since rotated.
        var dbName = $"seeder-preserve-pw-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig(password: "OriginalSeedPw"));

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
            db.Drivers.Add(new DriverDbRecord
            {
                Id = Guid.NewGuid(),
                UserName = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("rotated-by-operator"),
                Role = "admin",
                FirstName = "Existing",
                MiddleName = string.Empty,
                LastName = "Admin",
                Email = "existing@test.local",
                PhoneNumber = "555-9999",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            });
            db.SaveChanges();
        }

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var existing = verifyDb.Drivers.Single(d => d.UserName == "admin");

        Assert.True(BCrypt.Net.BCrypt.Verify("rotated-by-operator", existing.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("OriginalSeedPw", existing.PasswordHash));
        Assert.Equal("Existing", existing.FirstName); // other fields preserved
    }

    [Fact]
    public void EnsureAdminSeeded_ExistingNonAdminWithMatchingUserName_PromotedToAdmin()
    {
        var dbName = $"seeder-promote-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig());

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
            db.Drivers.Add(new DriverDbRecord
            {
                Id = Guid.NewGuid(),
                UserName = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("legacy-pw"),
                Role = "driver", // wrong role — promotion target
                FirstName = "Legacy",
                MiddleName = string.Empty,
                LastName = "User",
                Email = "legacy@test.local",
                PhoneNumber = "555-1111",
                CreatedAt = DateTime.UtcNow.AddDays(-7),
            });
            db.SaveChanges();
        }

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var promoted = verifyDb.Drivers.Single(d => d.UserName == "admin");

        Assert.Equal("admin", promoted.Role);
        // Password and other fields should remain untouched.
        Assert.True(BCrypt.Net.BCrypt.Verify("legacy-pw", promoted.PasswordHash));
        Assert.Equal("Legacy", promoted.FirstName);
    }

    [Fact]
    public void EnsureAdminSeeded_TestDriverWithAdminRole_DemotedBackToDriver()
    {
        var dbName = $"seeder-demote-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig());

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
            db.Drivers.Add(new DriverDbRecord
            {
                Id = Guid.NewGuid(),
                UserName = "test_driver",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("ignored"),
                Role = "admin", // accidentally elevated
                FirstName = "Test",
                MiddleName = string.Empty,
                LastName = "Driver",
                Email = "test@test.local",
                PhoneNumber = "555-2222",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
            });
            db.SaveChanges();
        }

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var resetDriver = verifyDb.Drivers.Single(d => d.UserName == "test_driver");

        Assert.Equal("driver", resetDriver.Role);
    }

    [Fact]
    public void EnsureAdminSeeded_TestDriverAlreadyDriver_UnchangedAndNoExtraWrites()
    {
        var dbName = $"seeder-noop-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig());

        var originalCreatedAt = DateTime.UtcNow.AddDays(-2);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
            db.Drivers.Add(new DriverDbRecord
            {
                Id = Guid.NewGuid(),
                UserName = "test_driver",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("ignored"),
                Role = "driver",
                FirstName = "Test",
                MiddleName = string.Empty,
                LastName = "Driver",
                Email = "test@test.local",
                PhoneNumber = "555-2222",
                CreatedAt = originalCreatedAt,
            });
            db.SaveChanges();
        }

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var unchanged = verifyDb.Drivers.Single(d => d.UserName == "test_driver");

        Assert.Equal("driver", unchanged.Role);
        Assert.Equal(originalCreatedAt, unchanged.CreatedAt);
    }

    [Fact]
    public void EnsureAdminSeeded_EmptyPassword_DoesNotCreateAdminButStillDemotesTestDriver()
    {
        // Operator-friendly path: leaving AdminSeed:Password blank in
        // appsettings should keep the seeder from creating a row with a
        // weak default password. The test_driver demotion must still run.
        var dbName = $"seeder-skip-create-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig(password: ""));

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
            db.Drivers.Add(new DriverDbRecord
            {
                Id = Guid.NewGuid(),
                UserName = "test_driver",
                PasswordHash = "irrelevant",
                Role = "admin",
                FirstName = "Test",
                LastName = "Driver",
                Email = "test@test.local",
                PhoneNumber = "555",
                CreatedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        Assert.False(verifyDb.Drivers.Any(d => d.UserName == "admin"));
        Assert.Equal("driver", verifyDb.Drivers.Single(d => d.UserName == "test_driver").Role);
    }

    [Fact]
    public void EnsureAdminSeeded_RunningTwice_IsIdempotent()
    {
        var dbName = $"seeder-idempotent-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig(password: "FirstRunPw"));

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);
        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        Assert.Equal(1, verifyDb.Drivers.Count(d => d.UserName == "admin"));
    }

    [Fact]
    public void EnsureAdminSeeded_CustomUserName_RespectsConfig()
    {
        var dbName = $"seeder-custom-name-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(
            dbName,
            AdminSeedConfig(userName: "ops-bootstrap", password: "OpsPw123"));

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        Assert.False(verifyDb.Drivers.Any(d => d.UserName == "admin"));
        var customAdmin = verifyDb.Drivers.Single(d => d.UserName == "ops-bootstrap");
        Assert.Equal("admin", customAdmin.Role);
    }
}
