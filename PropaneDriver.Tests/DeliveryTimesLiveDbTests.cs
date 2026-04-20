using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Tests;

public class DeliveryTimesLiveDbTests : IAsyncLifetime
{
    private PropaneDriverDbContext? _db;

    private static string? GetPassword() => Environment.GetEnvironmentVariable("PROPANE_SQL_PASSWORD");

    private static PropaneDriverDbContext CreateLiveDb(string password)
    {
        var connectionString =
            $"Server=tcp:sql-database-server-data-drive.database.windows.net,1433;" +
            $"Database=sql-db-data-drive;" +
            $"User ID=e_d_johnson;Password={password};" +
            $"Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

        var options = new DbContextOptionsBuilder<PropaneDriverDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 6,
                maxRetryDelay: TimeSpan.FromSeconds(15),
                errorNumbersToAdd: null))
            .Options;

        return new PropaneDriverDbContext(options);
    }

    public Task InitializeAsync()
    {
        var password = GetPassword();
        if (password is not null)
        {
            _db = CreateLiveDb(password);
        }
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
    }

    [Fact]
    public async Task CanConnect_And_QueryDeliveryTimes()
    {
        if (_db is null) return;

        var count = await _db.DeliveryTimes.CountAsync();
        Assert.True(count >= 0);
    }

    [Fact]
    public async Task InsertAndDelete_DeliveryTime_RoundTrips()
    {
        if (_db is null) return;

        var entity = new DeliveryTimeEntity
        {
            DeliveryId = "test-integration",
            Street = "INTEGRATION TEST",
            City = "TestCity",
            State = "MN",
            ZipCode = "00000",
            Latitude = 0.0,
            Longitude = 0.0,
            TimeIntervalSeconds = 1.23,
            RecordedAt = DateTime.UtcNow
        };

        _db.DeliveryTimes.Add(entity);
        await _db.SaveChangesAsync();

        Assert.True(entity.Id > 0, "Insert should generate an Id");

        var saved = await _db.DeliveryTimes.FindAsync(entity.Id);
        Assert.NotNull(saved);
        Assert.Equal("test-integration", saved.DeliveryId);
        Assert.Equal(1.23, saved.TimeIntervalSeconds);

        // Clean up
        _db.DeliveryTimes.Remove(saved);
        await _db.SaveChangesAsync();

        var deleted = await _db.DeliveryTimes.FindAsync(entity.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task AverageQuery_MatchesServerLogic()
    {
        if (_db is null) return;

        var uniqueStreet = $"INTEGRATION TEST {Guid.NewGuid()}";
        _db.DeliveryTimes.Add(new DeliveryTimeEntity
        {
            DeliveryId = "avg-test-1", Street = uniqueStreet, City = "TestCity", State = "MN", ZipCode = "00000",
            Latitude = 0, Longitude = 0,
            TimeIntervalSeconds = 40, RecordedAt = DateTime.UtcNow
        });
        _db.DeliveryTimes.Add(new DeliveryTimeEntity
        {
            DeliveryId = "avg-test-2", Street = uniqueStreet, City = "TestCity", State = "MN", ZipCode = "00000",
            Latitude = 0, Longitude = 0,
            TimeIntervalSeconds = 60, RecordedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var times = await _db.DeliveryTimes
            .Where(t => t.Street == uniqueStreet && t.City == "TestCity" && t.State == "MN" && t.ZipCode == "00000")
            .Select(t => t.TimeIntervalSeconds)
            .ToListAsync();

        Assert.Equal(2, times.Count);
        Assert.Equal(50.0, times.Average());

        // Clean up
        var testRows = await _db.DeliveryTimes.Where(t => t.Street == uniqueStreet).ToListAsync();
        _db.DeliveryTimes.RemoveRange(testRows);
        await _db.SaveChangesAsync();
    }
}
