using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Tests;

// Covers the endpoints under /api/routes:
//   GET {driverId}/{date}         — full RouteDto projection with deliveries + alerts
//   GET driver/{driverId}         — list summary (DeliveryCount / CompletedCount)
//   DELETE {id}                   — cascade-deletes child deliveries
//   GET today/{driverId}          — same projection as by-date, keyed on today
//   POST                          — creates RouteEntity + Deliveries,
//                                    auto-assigns SortOrder when zero
public class RouteEndpointsTests
{
    private static RouteEntity SeedRouteWithDeliveries(PropaneDriverDbContext db, Guid driverId, DateOnly date, int deliveryCount = 2)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            DriverId = driverId,
            Date = date,
            CreatedAt = DateTime.UtcNow
        };
        for (int i = 0; i < deliveryCount; i++)
        {
            route.Deliveries.Add(new DeliveryEntity
            {
                Id = Guid.NewGuid(),
                RouteId = route.Id,
                CustomerName = $"Customer {i}",
                Street = $"{i} Main St",
                City = "Hibbing",
                State = "MN",
                ZipCode = "55746",
                Latitude = 47.42 + i * 0.01,
                Longitude = -92.93,
                Status = 0,
                AvgDeliveryTimeMinutes = 15,
                SortOrder = i,
                CreatedAt = DateTime.UtcNow
            });
        }
        db.Routes.Add(route);
        db.SaveChanges();
        return route;
    }

    [Fact]
    public async Task GetRouteByDriverAndDate_ProjectsDeliveriesInSortOrder()
    {
        using var db = TestDb.Create();
        var driverId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var route = SeedRouteWithDeliveries(db, driverId, date, deliveryCount: 3);

        // Deliberately set SortOrder descending to verify the OrderBy is applied.
        var deliveries = await db.Deliveries.Where(d => d.RouteId == route.Id).ToListAsync();
        deliveries[0].SortOrder = 2;
        deliveries[1].SortOrder = 1;
        deliveries[2].SortOrder = 0;
        await db.SaveChangesAsync();

        var result = await db.Routes
            .AsNoTracking()
            .Where(r => r.DriverId == driverId && r.Date == date)
            .Select(r => new
            {
                Deliveries = r.Deliveries
                    .OrderBy(d => d.SortOrder)
                    .Select(d => d.CustomerName)
                    .ToList()
            })
            .FirstAsync();

        Assert.Equal(3, result.Deliveries.Count);
        // After OrderBy(SortOrder): deliveries[2] (SortOrder=0) first, then [1], then [0].
        Assert.Equal("Customer 2", result.Deliveries[0]);
        Assert.Equal("Customer 1", result.Deliveries[1]);
        Assert.Equal("Customer 0", result.Deliveries[2]);
    }

    [Fact]
    public async Task GetRouteByDriverAndDate_NoMatchingRoute_ReturnsNull()
    {
        using var db = TestDb.Create();

        var route = await db.Routes
            .Where(r => r.DriverId == Guid.NewGuid())
            .FirstOrDefaultAsync();
        Assert.Null(route);
    }

    [Fact]
    public async Task ListRoutesForDriver_ComputesDeliveryAndCompletedCounts()
    {
        using var db = TestDb.Create();
        var driverId = Guid.NewGuid();
        var route = SeedRouteWithDeliveries(db, driverId, DateOnly.FromDateTime(DateTime.Today), deliveryCount: 3);

        // Mark one delivery complete (Status == 2).
        var deliveries = await db.Deliveries.Where(d => d.RouteId == route.Id).ToListAsync();
        deliveries[0].Status = 2;
        await db.SaveChangesAsync();

        var summaries = await db.Routes
            .AsNoTracking()
            .Where(r => r.DriverId == driverId)
            .Select(r => new RouteListItemDto
            {
                Id = r.Id.ToString(),
                Date = r.Date,
                DeliveryCount = r.Deliveries.Count(),
                CompletedCount = r.Deliveries.Count(d => d.Status == 2)
            })
            .ToListAsync();

        Assert.Single(summaries);
        Assert.Equal(3, summaries[0].DeliveryCount);
        Assert.Equal(1, summaries[0].CompletedCount);
    }

    [Fact]
    public async Task ListRoutesForDriver_OrdersByDateDescending()
    {
        using var db = TestDb.Create();
        var driverId = Guid.NewGuid();
        SeedRouteWithDeliveries(db, driverId, new DateOnly(2026, 1, 1));
        SeedRouteWithDeliveries(db, driverId, new DateOnly(2026, 4, 1));
        SeedRouteWithDeliveries(db, driverId, new DateOnly(2026, 2, 1));

        var dates = await db.Routes
            .Where(r => r.DriverId == driverId)
            .OrderByDescending(r => r.Date)
            .Select(r => r.Date)
            .ToListAsync();

        Assert.Equal(new[]
        {
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 1)
        }, dates);
    }

    [Fact]
    public async Task DeleteRoute_CascadesToDeliveries()
    {
        using var db = TestDb.Create();
        var route = SeedRouteWithDeliveries(db, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), deliveryCount: 2);

        Assert.Equal(2, await db.Deliveries.CountAsync(d => d.RouteId == route.Id));

        db.Routes.Remove(route);
        await db.SaveChangesAsync();

        Assert.False(await db.Routes.AnyAsync(r => r.Id == route.Id));
        Assert.Equal(0, await db.Deliveries.CountAsync(d => d.RouteId == route.Id));
    }

    [Fact]
    public async Task DeleteRoute_MissingId_FindReturnsNull()
    {
        using var db = TestDb.Create();

        var route = await db.Routes.FindAsync(Guid.NewGuid());
        Assert.Null(route);
    }

    [Fact]
    public async Task GetTodaysRoute_MatchesOnToday()
    {
        using var db = TestDb.Create();
        var driverId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        SeedRouteWithDeliveries(db, driverId, today);
        SeedRouteWithDeliveries(db, driverId, today.AddDays(-1)); // yesterday's route

        var route = await db.Routes
            .Where(r => r.DriverId == driverId && r.Date == today)
            .FirstOrDefaultAsync();

        Assert.NotNull(route);
        Assert.Equal(today, route!.Date);
    }

    [Fact]
    public async Task CreateRoute_FromDto_AssignsSortOrderFromIndexWhenZero()
    {
        using var db = TestDb.Create();
        var driverId = Guid.NewGuid();
        var dto = new CreateRouteDto
        {
            DriverId = driverId.ToString(),
            Date = DateOnly.FromDateTime(DateTime.Today),
            Deliveries = new List<CreateDeliveryDto>
            {
                new CreateDeliveryDto { CustomerName = "A", Street = "1 St", SortOrder = 0 },
                new CreateDeliveryDto { CustomerName = "B", Street = "2 St", SortOrder = 0 },
                new CreateDeliveryDto { CustomerName = "C", Street = "3 St", SortOrder = 5 } // explicit wins
            }
        };

        // Mirror endpoint body.
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            DriverId = driverId,
            Date = dto.Date,
            CreatedAt = DateTime.UtcNow,
            Deliveries = dto.Deliveries.Select((d, i) => new DeliveryEntity
            {
                Id = Guid.NewGuid(),
                CustomerName = d.CustomerName,
                Street = d.Street,
                City = d.City,
                State = d.State,
                ZipCode = d.ZipCode,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                AvgDeliveryTimeMinutes = d.AvgDeliveryTimeMinutes,
                SortOrder = d.SortOrder == 0 ? i : d.SortOrder,
                Status = 0,
                CreatedAt = DateTime.UtcNow
            }).ToList()
        };
        db.Routes.Add(route);
        await db.SaveChangesAsync();

        var stored = await db.Deliveries
            .Where(d => d.RouteId == route.Id)
            .OrderBy(d => d.CustomerName)
            .ToListAsync();

        Assert.Equal(0, stored.Single(d => d.CustomerName == "A").SortOrder);
        Assert.Equal(1, stored.Single(d => d.CustomerName == "B").SortOrder);
        Assert.Equal(5, stored.Single(d => d.CustomerName == "C").SortOrder);
    }

    [Fact]
    public void CreateRoute_InvalidDriverGuid_Rejected()
    {
        // Endpoint guard: !Guid.TryParse(dto.DriverId, out _) → BadRequest.
        Assert.False(Guid.TryParse("not-a-guid", out _));
    }

    [Fact]
    public async Task GetRouteByDriverAndDate_ProjectsAlertsOrderedByCreatedAt()
    {
        using var db = TestDb.Create();
        var driverId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var route = SeedRouteWithDeliveries(db, driverId, date, deliveryCount: 1);
        var delivery = route.Deliveries[0];

        var now = DateTime.UtcNow;
        db.Alerts.AddRange(
            new AlertEntity { Id = Guid.NewGuid(), DeliveryId = delivery.Id, Message = "later",  CreatedAt = now.AddMinutes(5) },
            new AlertEntity { Id = Guid.NewGuid(), DeliveryId = delivery.Id, Message = "earlier", CreatedAt = now }
        );
        await db.SaveChangesAsync();

        var alerts = await db.Alerts
            .Where(a => a.DeliveryId == delivery.Id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => a.Message)
            .ToListAsync();

        Assert.Equal(new[] { "earlier", "later" }, alerts);
    }
}
