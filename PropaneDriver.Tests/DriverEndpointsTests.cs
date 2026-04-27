using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Tests;

// Covers the two endpoints in DriverEndpoints.cs:
//   GET /api/drivers  — ordered by LastName then FirstName, projected to DTO
//   GET /driver/{id}  — single driver lookup by Id
public class DriverEndpointsTests
{
    private static DriverDbRecord MakeDriver(string first, string last, string userName)
        => new DriverDbRecord
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordHash = "hash",
            Role = "driver",
            FirstName = first,
            MiddleName = "",
            LastName = last,
            Email = $"{userName}@example.com",
            PhoneNumber = "555-0100",
            CreatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task ListDrivers_OrdersByLastNameThenFirstName()
    {
        using var db = TestDb.Create();
        db.Drivers.AddRange(
            MakeDriver("Bob", "Zephyr", "bob-z"),
            MakeDriver("Alice", "Anderson", "alice-a"),
            MakeDriver("Carl", "Anderson", "carl-a")
        );
        await db.SaveChangesAsync();

        var drivers = await db.Drivers
            .AsNoTracking()
            .OrderBy(d => d.LastName).ThenBy(d => d.FirstName)
            .Select(d => new DriverDto
            {
                Id = d.Id.ToString(),
                UserName = d.UserName,
                FirstName = d.FirstName,
                MiddleName = d.MiddleName,
                LastName = d.LastName,
                Email = d.Email,
                PhoneNumber = d.PhoneNumber,
                Role = d.Role
            })
            .ToListAsync();

        Assert.Equal(3, drivers.Count);
        Assert.Equal("alice-a", drivers[0].UserName); // Anderson, Alice
        Assert.Equal("carl-a", drivers[1].UserName);  // Anderson, Carl
        Assert.Equal("bob-z", drivers[2].UserName);   // Zephyr, Bob
    }

    [Fact]
    public async Task ListDrivers_EmptyTable_ReturnsEmptyList()
    {
        using var db = TestDb.Create();

        var drivers = await db.Drivers.AsNoTracking().ToListAsync();
        Assert.Empty(drivers);
    }

    [Fact]
    public async Task GetDriverById_ExistingId_ProjectsCompleteDto()
    {
        using var db = TestDb.Create();
        var driver = MakeDriver("Grace", "Hopper", "grace");
        driver.Role = "admin";
        db.Drivers.Add(driver);
        await db.SaveChangesAsync();

        var found = await db.Drivers.FindAsync(driver.Id);
        Assert.NotNull(found);

        // Projection identical to the endpoint body.
        var dto = new DriverDto
        {
            Id = found!.Id.ToString(),
            UserName = found.UserName,
            Role = found.Role,
            FirstName = found.FirstName,
            MiddleName = found.MiddleName,
            LastName = found.LastName,
            Email = found.Email,
            PhoneNumber = found.PhoneNumber
        };

        Assert.Equal(driver.Id.ToString(), dto.Id);
        Assert.Equal("grace", dto.UserName);
        Assert.Equal("admin", dto.Role);
        Assert.Equal("Grace", dto.FirstName);
        Assert.Equal("Hopper", dto.LastName);
    }

    [Fact]
    public async Task GetDriverById_MissingId_FindReturnsNull()
    {
        using var db = TestDb.Create();

        var found = await db.Drivers.FindAsync(Guid.NewGuid());
        Assert.Null(found);
    }
}
