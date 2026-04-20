using PropaneDriver.Server.Data;
using PropaneDriver.Server.Services;

namespace PropaneDriver.Tests;

// Verifies the math in GPSHelperService.GetEstimatedRouteTime:
// servicing-time summation (with the 10-minute default for unstored),
// ordering by SortOrder, and the Haversine+winding+speed drive-leg
// estimate. Values use real-ish rural-MN coordinates to catch regressions
// in the distance formula.
public class GPSHelperServiceTests
{
    // Constants mirrored from GPSHelperService to compute expected values.
    private const double RoadWindingFactor = 1.3;
    private const double AverageMph = 40.0;
    private const double DefaultDeliveryMinutes = 10.0;

    // Creates a matched (DeliveryEntity, AddressEntity) pair for a stop.
    private static (DeliveryEntity delivery, AddressEntity address) Stop(
        int sortOrder, double lat, double lng, double avgMinutes = 0)
    {
        var address = new AddressEntity
        {
            Id = Guid.NewGuid(),
            Street = $"Stop-{sortOrder} St",
            City = "Hibbing",
            State = "MN",
            ZipCode = "55746",
            Latitude = lat,
            Longitude = lng
        };
        var delivery = new DeliveryEntity
        {
            Id = Guid.NewGuid(),
            AddressId = address.Id,
            SortOrder = sortOrder,
            AvgDeliveryTimeMinutes = avgMinutes,
            CustomerName = $"Stop-{sortOrder}",
            CreatedAt = DateTime.UtcNow
        };
        return (delivery, address);
    }

    private static async Task<int> Estimate(params (DeliveryEntity d, AddressEntity a)[] stops)
        => await GPSHelperService.GetEstimatedRouteTime(
            stops.Select(s => s.d).ToList(),
            stops.Select(s => s.a).ToList());

    [Fact]
    public async Task GetEstimatedRouteTime_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, await GPSHelperService.GetEstimatedRouteTime(
            new List<DeliveryEntity>(), new List<AddressEntity>()));
    }

    [Fact]
    public async Task GetEstimatedRouteTime_NullList_ReturnsZero()
    {
        Assert.Equal(0, await GPSHelperService.GetEstimatedRouteTime(
            null!, new List<AddressEntity>()));
    }

    [Fact]
    public async Task GetEstimatedRouteTime_SingleDelivery_NoDriveLeg_OnlyServicingTime()
    {
        Assert.Equal(15, await Estimate(Stop(0, 47.42, -92.93, avgMinutes: 15)));
    }

    [Fact]
    public async Task GetEstimatedRouteTime_MissingAvgTime_DefaultsTo10Minutes()
    {
        // Identical coords → 0 drive time; 10 + 10 = 20 min.
        Assert.Equal(20, await Estimate(
            Stop(0, 47.42, -92.93, avgMinutes: 0),
            Stop(1, 47.42, -92.93, avgMinutes: 0)));
    }

    [Fact]
    public async Task GetEstimatedRouteTime_NegativeAvgTime_TreatedAsUnsetAndDefaulted()
    {
        Assert.Equal(10, await Estimate(Stop(0, 47.0, -93.0, avgMinutes: -5)));
    }

    [Fact]
    public async Task GetEstimatedRouteTime_TwoStops_AddsHaversineDriveLeg()
    {
        // Hibbing, MN ≈ (47.4271, -92.9377)
        // Chisholm, MN ≈ (47.4893, -92.8838) — about 5.2 mi straight-line
        var expectedDriveMiles = HaversineMiles(47.4271, -92.9377, 47.4893, -92.8838) * RoadWindingFactor;
        var expectedDriveMinutes = expectedDriveMiles / AverageMph * 60.0;
        var expectedTotal = (int)Math.Round(12 + 8 + expectedDriveMinutes);

        Assert.Equal(expectedTotal, await Estimate(
            Stop(0, 47.4271, -92.9377, avgMinutes: 12),
            Stop(1, 47.4893, -92.8838, avgMinutes: 8)));
    }

    [Fact]
    public async Task GetEstimatedRouteTime_IdenticalCoordinates_ZeroDriveLeg()
    {
        Assert.Equal(10, await Estimate(
            Stop(0, 47.0, -93.0, avgMinutes: 5),
            Stop(1, 47.0, -93.0, avgMinutes: 5)));
    }

    [Fact]
    public async Task GetEstimatedRouteTime_RespectsSortOrder_NotInputOrder()
    {
        // Input deliberately out of order; function must sort before computing.
        var A = Stop(0, 47.0, -93.0, avgMinutes: 10);
        var B = Stop(1, 47.2, -93.0, avgMinutes: 10);
        var C = Stop(2, 47.4, -93.0, avgMinutes: 10);

        var inOrder  = await GPSHelperService.GetEstimatedRouteTime(
            new[] { A.delivery, B.delivery, C.delivery }.ToList(),
            new[] { A.address,  B.address,  C.address  }.ToList());
        var shuffled = await GPSHelperService.GetEstimatedRouteTime(
            new[] { C.delivery, A.delivery, B.delivery }.ToList(),
            new[] { C.address,  A.address,  B.address  }.ToList());

        Assert.Equal(inOrder, shuffled);
    }

    [Fact]
    public async Task GetEstimatedRouteTime_StopWithZeroCoords_SkipsLegDoesNotDoubleCountServicing()
    {
        // Middle stop has no GPS; drive legs touching it are skipped, but
        // servicing time is still counted.
        Assert.Equal(15, await Estimate(
            Stop(0, 47.0, -93.0, avgMinutes: 5),
            Stop(1, 0,    0,     avgMinutes: 5),
            Stop(2, 47.2, -93.0, avgMinutes: 5)));
    }

    [Fact]
    public async Task GetEstimatedRouteTime_ThreeStops_SumsBothDriveLegs()
    {
        var leg1 = HaversineMiles(47.42, -92.93, 47.48, -92.88) * RoadWindingFactor / AverageMph * 60.0;
        var leg2 = HaversineMiles(47.48, -92.88, 47.52, -92.82) * RoadWindingFactor / AverageMph * 60.0;
        var expected = (int)Math.Round(30 + leg1 + leg2);

        Assert.Equal(expected, await Estimate(
            Stop(0, 47.42, -92.93, avgMinutes: 10),
            Stop(1, 47.48, -92.88, avgMinutes: 10),
            Stop(2, 47.52, -92.82, avgMinutes: 10)));
    }

    // Duplicate of the private helper so we can compute expected values.
    private static double HaversineMiles(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusMiles = 3958.7613;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLng = (lng2 - lng1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMiles * c;
    }
}
