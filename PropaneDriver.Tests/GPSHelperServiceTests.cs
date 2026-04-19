using PropaneDriver.Server.Data;
using PropaneDriver.Server.Services;

namespace PropaneDriver.Tests;

// Verifies the pure math in GPSHelperService.GetEstimatedRouteTime:
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

    private static DeliveryEntity Stop(int sortOrder, double lat, double lng, double avgMinutes = 0)
        => new DeliveryEntity
        {
            Id = Guid.NewGuid(),
            SortOrder = sortOrder,
            Latitude = lat,
            Longitude = lng,
            AvgDeliveryTimeMinutes = avgMinutes,
            CustomerName = $"Stop-{sortOrder}",
            CreatedAt = DateTime.UtcNow
        };

    [Fact]
    public void GetEstimatedRouteTime_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, GPSHelperService.GetEstimatedRouteTime(new List<DeliveryEntity>()));
    }

    [Fact]
    public void GetEstimatedRouteTime_NullList_ReturnsZero()
    {
        Assert.Equal(0, GPSHelperService.GetEstimatedRouteTime(null!));
    }

    [Fact]
    public void GetEstimatedRouteTime_SingleDelivery_NoDriveLeg_OnlyServicingTime()
    {
        var stops = new List<DeliveryEntity> { Stop(0, 47.42, -92.93, avgMinutes: 15) };
        Assert.Equal(15, GPSHelperService.GetEstimatedRouteTime(stops));
    }

    [Fact]
    public void GetEstimatedRouteTime_MissingAvgTime_DefaultsTo10Minutes()
    {
        var stops = new List<DeliveryEntity>
        {
            Stop(0, 47.42, -92.93, avgMinutes: 0),
            Stop(1, 47.42, -92.93, avgMinutes: 0) // identical coords → 0 drive time
        };
        // 10 + 10 = 20 min
        Assert.Equal(20, GPSHelperService.GetEstimatedRouteTime(stops));
    }

    [Fact]
    public void GetEstimatedRouteTime_NegativeAvgTime_TreatedAsUnsetAndDefaulted()
    {
        var stops = new List<DeliveryEntity>
        {
            Stop(0, 47.0, -93.0, avgMinutes: -5) // invalid sentinel
        };
        Assert.Equal(10, GPSHelperService.GetEstimatedRouteTime(stops));
    }

    [Fact]
    public void GetEstimatedRouteTime_TwoStops_AddsHaversineDriveLeg()
    {
        // Hibbing, MN ≈ (47.4271, -92.9377)
        // Chisholm, MN ≈ (47.4893, -92.8838) — about 5.2 mi straight-line
        var stops = new List<DeliveryEntity>
        {
            Stop(0, 47.4271, -92.9377, avgMinutes: 12),
            Stop(1, 47.4893, -92.8838, avgMinutes: 8)
        };

        var expectedDriveMiles = HaversineMiles(47.4271, -92.9377, 47.4893, -92.8838) * RoadWindingFactor;
        var expectedDriveMinutes = expectedDriveMiles / AverageMph * 60.0;
        var expectedTotal = (int)Math.Round(12 + 8 + expectedDriveMinutes);

        Assert.Equal(expectedTotal, GPSHelperService.GetEstimatedRouteTime(stops));
    }

    [Fact]
    public void GetEstimatedRouteTime_IdenticalCoordinates_ZeroDriveLeg()
    {
        var stops = new List<DeliveryEntity>
        {
            Stop(0, 47.0, -93.0, avgMinutes: 5),
            Stop(1, 47.0, -93.0, avgMinutes: 5)
        };
        Assert.Equal(10, GPSHelperService.GetEstimatedRouteTime(stops));
    }

    [Fact]
    public void GetEstimatedRouteTime_RespectsSortOrder_NotInputOrder()
    {
        // Input deliberately out of order; ordered sequence should be A → B → C.
        // With distinct coords, different orderings would yield different
        // drive totals, but the function must sort before computing so the
        // result is independent of the list's original order.
        var A = Stop(0, 47.0, -93.0, avgMinutes: 10);
        var B = Stop(1, 47.2, -93.0, avgMinutes: 10);
        var C = Stop(2, 47.4, -93.0, avgMinutes: 10);

        var inOrder = GPSHelperService.GetEstimatedRouteTime(new List<DeliveryEntity> { A, B, C });
        var shuffled = GPSHelperService.GetEstimatedRouteTime(new List<DeliveryEntity> { C, A, B });

        Assert.Equal(inOrder, shuffled);
    }

    [Fact]
    public void GetEstimatedRouteTime_StopWithZeroCoords_SkipsLegDoesNotDoubleCountServicing()
    {
        // Middle stop has no GPS. We should still count its servicing time,
        // but the two drive-legs touching it should be skipped rather than
        // giving bogus distances (to (0,0), which is off the coast of Africa).
        var stops = new List<DeliveryEntity>
        {
            Stop(0, 47.0, -93.0, avgMinutes: 5),
            Stop(1, 0, 0,       avgMinutes: 5), // unset coords
            Stop(2, 47.2, -93.0, avgMinutes: 5)
        };

        Assert.Equal(15, GPSHelperService.GetEstimatedRouteTime(stops));
    }

    [Fact]
    public void GetEstimatedRouteTime_ThreeStops_SumsBothDriveLegs()
    {
        var stops = new List<DeliveryEntity>
        {
            Stop(0, 47.42, -92.93, avgMinutes: 10),
            Stop(1, 47.48, -92.88, avgMinutes: 10),
            Stop(2, 47.52, -92.82, avgMinutes: 10)
        };

        var leg1 = HaversineMiles(47.42, -92.93, 47.48, -92.88) * RoadWindingFactor / AverageMph * 60.0;
        var leg2 = HaversineMiles(47.48, -92.88, 47.52, -92.82) * RoadWindingFactor / AverageMph * 60.0;
        var expected = (int)Math.Round(30 + leg1 + leg2);

        Assert.Equal(expected, GPSHelperService.GetEstimatedRouteTime(stops));
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
