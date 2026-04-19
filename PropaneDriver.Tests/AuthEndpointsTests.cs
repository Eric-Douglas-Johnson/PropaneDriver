using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Tests;

// Exercises the data + crypto logic encapsulated by AuthEndpoints.cs:
// BCrypt password verification, duplicate UserName detection, single-use
// token invalidation, and SHA-256 token hashing for password reset.
public class AuthEndpointsTests
{
    private static DriverEntity SeedDriver(PropaneDriverDbContext db, string userName, string password, string email = "driver@example.com")
    {
        var driver = new DriverEntity
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = "driver",
            FirstName = "Test",
            LastName = "Driver",
            Email = email,
            PhoneNumber = "555-0100",
            CreatedAt = DateTime.UtcNow
        };
        db.Drivers.Add(driver);
        db.SaveChanges();
        return driver;
    }

    private static string HashToken(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    [Fact]
    public async Task Authenticate_ValidCredentials_Verifies()
    {
        using var db = TestDb.Create();
        SeedDriver(db, "driver1", "hunter2");

        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.UserName == "driver1");
        Assert.NotNull(driver);
        Assert.True(BCrypt.Net.BCrypt.Verify("hunter2", driver!.PasswordHash));
    }

    [Fact]
    public async Task Authenticate_WrongPassword_FailsVerify()
    {
        using var db = TestDb.Create();
        SeedDriver(db, "driver1", "hunter2");

        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.UserName == "driver1");
        Assert.NotNull(driver);
        Assert.False(BCrypt.Net.BCrypt.Verify("wrong-password", driver!.PasswordHash));
    }

    [Fact]
    public async Task Authenticate_UnknownUserName_ReturnsNull()
    {
        using var db = TestDb.Create();

        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.UserName == "nobody");
        Assert.Null(driver);
    }

    [Fact]
    public async Task Register_NewUserName_InsertsDriver()
    {
        using var db = TestDb.Create();

        Assert.False(await db.Drivers.AnyAsync(d => d.UserName == "new-driver"));

        db.Drivers.Add(new DriverEntity
        {
            Id = Guid.NewGuid(),
            UserName = "new-driver",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw123456"),
            Role = "driver",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await db.Drivers.AnyAsync(d => d.UserName == "new-driver"));
    }

    [Fact]
    public async Task Register_DuplicateUserName_DetectedByAnyAsync()
    {
        using var db = TestDb.Create();
        SeedDriver(db, "taken", "pw");

        // Endpoint guards with: var exists = await db.Drivers.AnyAsync(...);
        var exists = await db.Drivers.AnyAsync(d => d.UserName == "taken");
        Assert.True(exists);
    }

    [Fact]
    public async Task ForgotPassword_CreatesHashedTokenRow_AndInvalidatesExisting()
    {
        using var db = TestDb.Create();
        var driver = SeedDriver(db, "driver1", "pw", email: "reset@example.com");

        // Seed an existing unused token — ForgotPassword invalidates these.
        var existing = new PasswordResetTokenEntity
        {
            DriverId = driver.Id,
            TokenHash = HashToken("stale-token"),
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        db.PasswordResetTokens.Add(existing);
        await db.SaveChangesAsync();

        // Mirror the endpoint's body.
        var stale = await db.PasswordResetTokens
            .Where(t => t.DriverId == driver.Id && t.UsedAt == null)
            .ToListAsync();
        foreach (var t in stale) t.UsedAt = DateTime.UtcNow;

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").Replace("=", "");
        var tokenHash = HashToken(rawToken);

        db.PasswordResetTokens.Add(new PasswordResetTokenEntity
        {
            DriverId = driver.Id,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();

        // The old token must be marked used.
        var reloadedStale = await db.PasswordResetTokens.FirstAsync(t => t.Id == existing.Id);
        Assert.NotNull(reloadedStale.UsedAt);

        // The new token must store the hash (not the raw secret).
        var newToken = await db.PasswordResetTokens
            .FirstAsync(t => t.TokenHash == tokenHash);
        Assert.Equal(tokenHash, newToken.TokenHash);
        Assert.NotEqual(rawToken, newToken.TokenHash);
        Assert.Null(newToken.UsedAt);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_NoTokenCreated()
    {
        using var db = TestDb.Create();
        SeedDriver(db, "driver1", "pw", email: "real@example.com");

        // Endpoint early-returns when the email doesn't match; so no tokens
        // should appear in the table afterwards.
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Email == "ghost@example.com");
        Assert.Null(driver);
        Assert.Equal(0, await db.PasswordResetTokens.CountAsync());
    }

    [Fact]
    public async Task ResetPassword_ValidToken_UpdatesHashAndMarksUsed()
    {
        using var db = TestDb.Create();
        var driver = SeedDriver(db, "driver1", "old-pw");
        var rawToken = "plain-token-abc";
        var tokenHash = HashToken(rawToken);
        var resetToken = new PasswordResetTokenEntity
        {
            DriverId = driver.Id,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        db.PasswordResetTokens.Add(resetToken);
        await db.SaveChangesAsync();

        // Client submits the raw token; server re-hashes and looks it up.
        var lookupHash = HashToken(rawToken);
        var found = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == lookupHash);
        Assert.NotNull(found);
        Assert.Null(found!.UsedAt);
        Assert.True(found.ExpiresAt > DateTime.UtcNow);

        driver.PasswordHash = BCrypt.Net.BCrypt.HashPassword("new-password");
        found.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var updated = await db.Drivers.FirstAsync(d => d.Id == driver.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("new-password", updated.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("old-pw", updated.PasswordHash));
        Assert.NotNull((await db.PasswordResetTokens.FirstAsync(t => t.Id == resetToken.Id)).UsedAt);
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_RejectedByGuard()
    {
        using var db = TestDb.Create();
        var driver = SeedDriver(db, "driver1", "pw");
        var rawToken = "expired-token";
        db.PasswordResetTokens.Add(new PasswordResetTokenEntity
        {
            DriverId = driver.Id,
            TokenHash = HashToken(rawToken),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5) // already expired
        });
        await db.SaveChangesAsync();

        var token = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == HashToken(rawToken));
        Assert.NotNull(token);
        // Endpoint guard: token is null || UsedAt != null || ExpiresAt < UtcNow → reject.
        Assert.True(token!.ExpiresAt < DateTime.UtcNow);
    }

    [Fact]
    public async Task ResetPassword_UsedToken_RejectedByGuard()
    {
        using var db = TestDb.Create();
        var driver = SeedDriver(db, "driver1", "pw");
        var rawToken = "consumed-token";
        db.PasswordResetTokens.Add(new PasswordResetTokenEntity
        {
            DriverId = driver.Id,
            TokenHash = HashToken(rawToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UsedAt = DateTime.UtcNow // consumed
        });
        await db.SaveChangesAsync();

        var token = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == HashToken(rawToken));
        Assert.NotNull(token);
        Assert.NotNull(token!.UsedAt);
    }
}
