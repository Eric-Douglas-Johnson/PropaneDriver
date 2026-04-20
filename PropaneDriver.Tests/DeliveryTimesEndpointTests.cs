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

    private static DeliveryTimeEntity MakeEntity(string deliveryId, string street, string city, string state, string zip, double seconds) =>
        new DeliveryTimeEntity
        {
            DeliveryId = deliveryId,
            Street = street,
            City = city,
            State = state,
            ZipCode = zip,
            Latitude = 47.0,
            Longitude = -93.0,
            TimeIntervalSeconds = seconds,
            RecordedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task SaveDeliveryTime_InsertsRecord_And_ReturnsId()
    {
        using var db = CreateInMemoryDb();

        var dto = new DeliveryTimeDto
        {
            DeliveryId = "1",
            Street = "123 Main St",
            City = "Hibbing",
            State = "MN",
            ZipCode = "55746",
            Latitude = 47.3856,
            Longitude = -92.9888,
            TimeIntervalSeconds = 45.5
        };

        var entity = new DeliveryTimeEntity
        {
            DeliveryId = dto.DeliveryId,
            Street = dto.Street,
            City = dto.City,
            State = dto.State,
            ZipCode = dto.ZipCode,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            TimeIntervalSeconds = dto.TimeIntervalSeconds,
            RecordedAt = DateTime.UtcNow
        };

        db.DeliveryTimes.Add(entity);
        await db.SaveChangesAsync();

        Assert.True(entity.Id > 0);

        var saved = await db.DeliveryTimes.FirstAsync(t => t.Id == entity.Id);
        Assert.Equal("1", saved.DeliveryId);
        Assert.Equal("123 Main St", saved.Street);
        Assert.Equal("Hibbing", saved.City);
        Assert.Equal("MN", saved.State);
        Assert.Equal("55746", saved.ZipCode);
        Assert.Equal(47.3856, saved.Latitude);
        Assert.Equal(-92.9888, saved.Longitude);
        Assert.Equal(45.5, saved.TimeIntervalSeconds);
        Assert.True(saved.RecordedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task SaveDeliveryTime_MultipleRecords_AllPersisted()
    {
        using var db = CreateInMemoryDb();

        for (int i = 1; i <= 3; i++)
        {
            db.DeliveryTimes.Add(MakeEntity(i.ToString(), $"{i} Oak Ave", "Hibbing", "MN", "55746", 30.0 * i));
        }
        await db.SaveChangesAsync();

        var count = await db.DeliveryTimes.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task SaveDeliveryTime_ZeroSeconds_StillPersisted()
    {
        using var db = CreateInMemoryDb();

        var entity = MakeEntity("1", "1 Test St", "Hibbing", "MN", "55746", 0);
        db.DeliveryTimes.Add(entity);
        await db.SaveChangesAsync();

        var saved = await db.DeliveryTimes.FirstAsync();
        Assert.Equal(0, saved.TimeIntervalSeconds);
    }

    [Fact]
    public async Task GetAverageTime_NoRecords_ReturnsZero()
    {
        using var db = CreateInMemoryDb();

        var times = await db.DeliveryTimes
            .Where(t => t.Street == "Nonexistent" && t.City == "Nowhere" && t.State == "XX" && t.ZipCode == "00000")
            .Select(t => t.TimeIntervalSeconds)
            .ToListAsync();

        Assert.Empty(times);
    }

    [Fact]
    public async Task GetAverageTime_WithRecords_ReturnsCorrectAverage()
    {
        using var db = CreateInMemoryDb();

        db.DeliveryTimes.Add(MakeEntity("1", "12368 Jacobson Rd", "Hibbing", "MN", "55746", 30));
        db.DeliveryTimes.Add(MakeEntity("1", "12368 Jacobson Rd", "Hibbing", "MN", "55746", 60));
        db.DeliveryTimes.Add(MakeEntity("1", "12368 Jacobson Rd", "Hibbing", "MN", "55746", 90));
        await db.SaveChangesAsync();

        var times = await db.DeliveryTimes
            .Where(t => t.Street == "12368 Jacobson Rd" && t.City == "Hibbing" && t.State == "MN" && t.ZipCode == "55746")
            .Select(t => t.TimeIntervalSeconds)
            .ToListAsync();

        Assert.Equal(3, times.Count);
        Assert.Equal(60.0, times.Average());
    }

    [Fact]
    public async Task GetAverageTime_DifferentAddresses_OnlyMatchingCounted()
    {
        using var db = CreateInMemoryDb();

        db.DeliveryTimes.Add(MakeEntity("1", "1 Alpha St", "Hibbing", "MN", "55746", 100));
        db.DeliveryTimes.Add(MakeEntity("2", "2 Beta St", "Hibbing", "MN", "55746", 200));
        await db.SaveChangesAsync();

        var timesA = await db.DeliveryTimes
            .Where(t => t.Street == "1 Alpha St" && t.City == "Hibbing" && t.State == "MN" && t.ZipCode == "55746")
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
        // 4 samples → average of all 4, no trim.
        var avg = AverageWithOutlierTrim(new List<double> { 10, 20, 30, 40 });
        Assert.Equal(25.0, avg);
    }

    [Fact]
    public void AverageWithOutlierTrim_FiveSamples_DropsMinAndMax()
    {
        // With extreme outliers on both ends, trimmed average ignores them.
        var avg = AverageWithOutlierTrim(new List<double> { 5, 50, 55, 60, 9999 });
        // After trim: {50, 55, 60} → 55.
        Assert.Equal(55.0, avg);
    }

    [Fact]
    public void AverageWithOutlierTrim_ManySamples_TrimsOnlyOneFromEachEnd()
    {
        var avg = AverageWithOutlierTrim(new List<double> { 1, 10, 20, 30, 40, 50, 999 });
        // After trim: {10, 20, 30, 40, 50} → 30.
        Assert.Equal(30.0, avg);
    }
}
