using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Tests;

// Covers the queries behind the two endpoints in DriverEndpoints.cs:
//   GET /api/drivers  — drivers only, ordered by LastName then FirstName
//   GET /driver/{id}  — single driver lookup by user Id
public class DriverEndpointsTests
{
    private static UserDbRecord AddUser(PropaneDriverDbContext db, string first, string last, string userName, bool isDriver = true)
    {
        var user = new UserDbRecord
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordHash = "hash",
            FirstName = first,
            MiddleName = "",
            LastName = last,
            Email = $"{userName}@example.com",
            PhoneNumber = "555-0100",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        if (isDriver)
            db.Drivers.Add(new DriverDbRecord { UserId = user.Id });
        return user;
    }

    // The join the endpoints run: a driver's profile lives on its Users row.
    private static IQueryable<UserDbRecord> DriverUsers(PropaneDriverDbContext db) =>
        db.Drivers.AsNoTracking().Join(db.Users, d => d.UserId, u => u.Id, (d, u) => u);

    [Fact]
    public async Task ListDrivers_OrdersByLastNameThenFirstName()
    {
        using var db = TestDb.Create();
        AddUser(db, "Bob", "Zephyr", "bob-z");
        AddUser(db, "Alice", "Anderson", "alice-a");
        AddUser(db, "Carl", "Anderson", "carl-a");
        await db.SaveChangesAsync();

        var drivers = await DriverUsers(db)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync();

        Assert.Equal(["alice-a", "carl-a", "bob-z"], drivers.Select(d => d.UserName));
    }

    [Fact]
    public async Task ListDrivers_ExcludesUsersWithoutTheDriverRole()
    {
        using var db = TestDb.Create();
        AddUser(db, "Dana", "Driver", "dana-driver");
        AddUser(db, "Sam", "Supervisor", "sam-supervisor", isDriver: false);
        await db.SaveChangesAsync();

        var drivers = await DriverUsers(db).ToListAsync();

        Assert.Equal(["dana-driver"], drivers.Select(d => d.UserName));
    }

    [Fact]
    public async Task ListDrivers_EmptyTable_ReturnsEmptyList()
    {
        using var db = TestDb.Create();

        Assert.Empty(await DriverUsers(db).ToListAsync());
    }

    [Fact]
    public async Task GetDriverById_ExistingDriver_ReturnsProfile()
    {
        using var db = TestDb.Create();
        var driver = AddUser(db, "Grace", "Hopper", "grace");
        await db.SaveChangesAsync();

        var found = await DriverUsers(db).FirstOrDefaultAsync(u => u.Id == driver.Id);

        Assert.NotNull(found);
        Assert.Equal("grace", found!.UserName);
        Assert.Equal("Grace", found.FirstName);
        Assert.Equal("Hopper", found.LastName);
    }

    [Fact]
    public async Task GetDriverById_UserWithoutDriverRole_ReturnsNull()
    {
        using var db = TestDb.Create();
        var supervisor = AddUser(db, "Sam", "Supervisor", "sam", isDriver: false);
        await db.SaveChangesAsync();

        Assert.Null(await DriverUsers(db).FirstOrDefaultAsync(u => u.Id == supervisor.Id));
    }

    [Fact]
    public async Task GetDriverById_MissingId_ReturnsNull()
    {
        using var db = TestDb.Create();

        Assert.Null(await DriverUsers(db).FirstOrDefaultAsync(u => u.Id == Guid.NewGuid()));
    }
}
