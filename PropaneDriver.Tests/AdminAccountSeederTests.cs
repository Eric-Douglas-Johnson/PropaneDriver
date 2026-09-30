using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PropaneDriver.Server.Data;
using PropaneDriver.Server.Services;

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

    // An account as it stands before the seeder runs.
    private static void AddExistingUser(
        IServiceProvider services,
        string userName,
        string password,
        string firstName,
        IEnumerable<string> roles,
        DateTime? createdAt = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        var user = new UserDbRecord
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FirstName = firstName,
            MiddleName = string.Empty,
            LastName = "Existing",
            Email = $"{userName}@test.local",
            PhoneNumber = "555-9999",
            CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-30),
        };
        db.Users.Add(user);
        RoleService(db).AddRoles(user.Id, roles);
        db.SaveChanges();
    }

    private static UserRoleService RoleService(PropaneDriverDbContext db) => new(db, new DriverHistoryService(db));

    private static List<string> RolesOf(PropaneDriverDbContext db, UserDbRecord user) =>
        RoleService(db).GetRolesAsync(user.Id).GetAwaiter().GetResult();

    [Fact]
    public void EnsureAdminSeeded_NoExistingRow_CreatesAdminWithHashedPassword()
    {
        var dbName = $"seeder-create-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig(password: "Initial!Pw"));

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var seeded = db.Users.Single(u => u.UserName == "admin");

        Assert.Equal(["admin"], RolesOf(db, seeded));
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
        AddExistingUser(services, "admin", "rotated-by-operator", "Existing", ["admin"]);

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var existing = verifyDb.Users.Single(u => u.UserName == "admin");

        Assert.True(BCrypt.Net.BCrypt.Verify("rotated-by-operator", existing.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("OriginalSeedPw", existing.PasswordHash));
        Assert.Equal("Existing", existing.FirstName); // other fields preserved
        Assert.Equal(1, verifyDb.Administrators.Count(a => a.UserId == existing.Id));
    }

    [Fact]
    public void EnsureAdminSeeded_ExistingNonAdminWithMatchingUserName_GainsAdminAndKeepsOtherRoles()
    {
        var dbName = $"seeder-promote-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig());
        AddExistingUser(services, "admin", "legacy-pw", "Legacy", ["driver"]);

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var promoted = verifyDb.Users.Single(u => u.UserName == "admin");

        Assert.Equal(["driver", "admin"], RolesOf(verifyDb, promoted));
        // Password and other fields should remain untouched.
        Assert.True(BCrypt.Net.BCrypt.Verify("legacy-pw", promoted.PasswordHash));
        Assert.Equal("Legacy", promoted.FirstName);
    }

    [Fact]
    public void EnsureAdminSeeded_TestDriverWithElevatedRoles_ResetToDriverOnly()
    {
        var dbName = $"seeder-demote-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig());
        AddExistingUser(services, "test_driver", "ignored", "Test", ["supervisor", "admin"]);

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var resetUser = verifyDb.Users.Single(u => u.UserName == "test_driver");

        Assert.Equal(["driver"], RolesOf(verifyDb, resetUser));
    }

    [Fact]
    public void EnsureAdminSeeded_TestDriverAlreadyDriver_UnchangedAndNoExtraWrites()
    {
        var dbName = $"seeder-noop-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig());
        var originalCreatedAt = DateTime.UtcNow.AddDays(-2);
        AddExistingUser(services, "test_driver", "ignored", "Test", ["driver"], originalCreatedAt);

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        var unchanged = verifyDb.Users.Single(u => u.UserName == "test_driver");

        Assert.Equal(["driver"], RolesOf(verifyDb, unchanged));
        Assert.Equal(originalCreatedAt, unchanged.CreatedAt);
    }

    [Fact]
    public void EnsureAdminSeeded_EmptyPassword_DoesNotCreateAdminButStillDemotesTestDriver()
    {
        // Operator-friendly path: leaving AdminSeed:Password blank should keep
        // the seeder from creating a row with a weak default password. The
        // test_driver demotion must still run.
        var dbName = $"seeder-skip-create-{Guid.NewGuid()}";
        var services = BuildServicesWithSharedDb(dbName, AdminSeedConfig(password: ""));
        AddExistingUser(services, "test_driver", "irrelevant", "Test", ["admin"]);

        AdminAccountSeeder.EnsureAdminSeeded(services, NullLogger.Instance);

        using var verifyScope = services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        Assert.False(verifyDb.Users.Any(u => u.UserName == "admin"));
        Assert.Equal(["driver"], RolesOf(verifyDb, verifyDb.Users.Single(u => u.UserName == "test_driver")));
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

        Assert.Equal(1, verifyDb.Users.Count(u => u.UserName == "admin"));
        Assert.Equal(1, verifyDb.Administrators.Count());
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

        Assert.False(verifyDb.Users.Any(u => u.UserName == "admin"));
        var customAdmin = verifyDb.Users.Single(u => u.UserName == "ops-bootstrap");
        Assert.Equal(["admin"], RolesOf(verifyDb, customAdmin));
    }
}
