using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Tests;

public class DeliveryTimesEndpointTests
{
    private static PropaneDriverDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<PropaneDriverDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PropaneDriverDbContext(options);
    }

    private static AddressDbRecord MakeAddress(string street = "123 Main St", string city = "Hibbing",
        string state = "MN", string zip = "55746") =>
        new AddressDbRecord
        {
            Id = Guid.NewGuid(),
            Street = street,
            City = city,
            State = state,
            ZipCode = zip,
            Latitude = 47.0,
            Longitude = -93.0,
            AvgDeliveryTimeMinutes = 0
        };

    private static DeliveryTimeDbRecord MakeEntity(Guid addressId, string deliveryId, double seconds) =>
        new DeliveryTimeDbRecord
        {
            DeliveryId = deliveryId,
            AddressId = addressId,
            TimeIntervalSeconds = seconds,
            RecordedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task SaveDeliveryTime_InsertsRecord_And_ReturnsId()
    {
        using var db = CreateInMemoryDb();

        var address = MakeAddress();
        db.Addresses.Add(address);
        await db.SaveChangesAsync();

        var dto = new DeliveryTimeDto
        {
            DeliveryId = "1",
            AddressId = address.Id,
            TimeIntervalSeconds = 45.5
        };

        var entity = new DeliveryTimeDbRecord
        {
            DeliveryId = dto.DeliveryId,
            AddressId = dto.AddressId,
            TimeIntervalSeconds = dto.TimeIntervalSeconds,
            RecordedAt = DateTime.UtcNow
        };

        db.DeliveryTimes.Add(entity);
        await db.SaveChangesAsync();

        Assert.True(entity.Id > 0);

        var saved = await db.DeliveryTimes.FirstAsync(t => t.Id == entity.Id);
        Assert.Equal("1", saved.DeliveryId);
        Assert.Equal(address.Id, saved.AddressId);
        Assert.Equal(45.5, saved.TimeIntervalSeconds);
        Assert.True(saved.RecordedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task SaveDeliveryTime_MultipleRecords_AllPersisted()
    {
        using var db = CreateInMemoryDb();

        var address = MakeAddress();
        db.Addresses.Add(address);
        await db.SaveChangesAsync();

        for (int i = 1; i <= 3; i++)
            db.DeliveryTimes.Add(MakeEntity(address.Id, i.ToString(), 30.0 * i));

        await db.SaveChangesAsync();

        var count = await db.DeliveryTimes.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task SaveDeliveryTime_ZeroSeconds_StillPersisted()
    {
        using var db = CreateInMemoryDb();

        var address = MakeAddress();
        db.Addresses.Add(address);
        await db.SaveChangesAsync();

        db.DeliveryTimes.Add(MakeEntity(address.Id, "1", 0));
        await db.SaveChangesAsync();

        var saved = await db.DeliveryTimes.FirstAsync();
        Assert.Equal(0, saved.TimeIntervalSeconds);
    }

    [Fact]
    public async Task GetAverageTime_NoRecords_ReturnsZero()
    {
        using var db = CreateInMemoryDb();

        var address = MakeAddress();
        db.Addresses.Add(address);
        await db.SaveChangesAsync();

        var times = await db.DeliveryTimes
            .Where(t => t.AddressId == address.Id)
            .Select(t => t.TimeIntervalSeconds)
            .ToListAsync();

        Assert.Empty(times);
    }

    [Fact]
    public async Task GetAverageTime_WithRecords_ReturnsCorrectAverage()
    {
        using var db = CreateInMemoryDb();

        var address = MakeAddress("12368 Jacobson Rd");
        db.Addresses.Add(address);
        await db.SaveChangesAsync();

        db.DeliveryTimes.Add(MakeEntity(address.Id, "1", 30));
        db.DeliveryTimes.Add(MakeEntity(address.Id, "2", 60));
        db.DeliveryTimes.Add(MakeEntity(address.Id, "3", 90));
        await db.SaveChangesAsync();

        var times = await db.DeliveryTimes
            .Where(t => t.AddressId == address.Id)
            .Select(t => t.TimeIntervalSeconds)
            .ToListAsync();

        Assert.Equal(3, times.Count);
        Assert.Equal(60.0, times.Average());
    }

    [Fact]
    public async Task GetAverageTime_DifferentAddresses_OnlyMatchingCounted()
    {
        using var db = CreateInMemoryDb();

        var addrA = MakeAddress("1 Alpha St");
        var addrB = MakeAddress("2 Beta St");
        db.Addresses.AddRange(addrA, addrB);
        await db.SaveChangesAsync();

        db.DeliveryTimes.Add(MakeEntity(addrA.Id, "1", 100));
        db.DeliveryTimes.Add(MakeEntity(addrB.Id, "2", 200));
        await db.SaveChangesAsync();

        var timesA = await db.DeliveryTimes
            .Where(t => t.AddressId == addrA.Id)
            .Select(t => t.TimeIntervalSeconds)
            .ToListAsync();

        Assert.Single(timesA);
        Assert.Equal(100.0, timesA.Average());
    }

    // Mirrors the outlier-trim logic in DeliveryTimeEndpoints.cs: once there
    // are more than 4 samples, drop the min and max before averaging.
    private static double AverageWithOutlierTrim(List<double> times)
    {
        if (times.Count == 0) return 0.0;
        times.Sort();
        if (times.Count > 4)
        {
            times.RemoveAt(times.Count - 1);
            times.RemoveAt(0);
        }
        return times.Average();
    }

    [Fact]
    public void AverageWithOutlierTrim_EmptyList_ReturnsZero()
    {
        Assert.Equal(0.0, AverageWithOutlierTrim(new List<double>()));
    }

    [Fact]
    public void AverageWithOutlierTrim_FourOrFewerSamples_NoTrim()
    {
        var avg = AverageWithOutlierTrim(new List<double> { 10, 20, 30, 40 });
        Assert.Equal(25.0, avg);
    }

    [Fact]
    public void AverageWithOutlierTrim_FiveSamples_DropsMinAndMax()
    {
        var avg = AverageWithOutlierTrim(new List<double> { 5, 50, 55, 60, 9999 });
        Assert.Equal(55.0, avg);
    }

    [Fact]
    public void AverageWithOutlierTrim_ManySamples_TrimsOnlyOneFromEachEnd()
    {
        var avg = AverageWithOutlierTrim(new List<double> { 1, 10, 20, 30, 40, 50, 999 });
        Assert.Equal(30.0, avg);
    }
}
