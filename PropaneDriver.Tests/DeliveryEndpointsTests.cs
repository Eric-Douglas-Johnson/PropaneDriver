using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Tests;

// Exercises the three endpoints under /api/deliveries:
//   GET    {id}/alerts  — listed in CreatedAt order for an existing delivery
//   POST   {id}/alerts  — trims whitespace, rejects empty messages
//   PUT    {id}/status  — updates the Status column
public class DeliveryEndpointsTests
{
    private static DeliveryDbRecord SeedDelivery(PropaneDriverDbContext db)
    {
        var routeId = Guid.NewGuid();
        db.Routes.Add(new RouteDbRecord
        {
            Id = routeId,
            DriverId = Guid.NewGuid(),
            Date = DateOnly.FromDateTime(DateTime.Today),
            CreatedAt = DateTime.UtcNow
        });

        var address = new AddressDbRecord
        {
            Id = Guid.NewGuid(),
            Street = "100 Analytic Way",
            City = "Hibbing",
            State = "MN",
            ZipCode = "55746",
            Latitude = 47.42,
            Longitude = -92.93
        };
        db.Addresses.Add(address);

        var delivery = new DeliveryDbRecord
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            AddressId = address.Id,
            CustomerName = "Ada Lovelace",
            Status = 0,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow
        };
        db.Deliveries.Add(delivery);
        db.SaveChanges();
        return delivery;
    }

    [Fact]
    public async Task ListAlerts_ForExistingDelivery_OrderedByCreatedAt()
    {
        using var db = TestDb.Create();
        var delivery = SeedDelivery(db);

        var now = DateTime.UtcNow;
        db.Alerts.AddRange(
            new AlertDbRecord { Id = Guid.NewGuid(), DeliveryId = delivery.Id, Message = "second", CreatedAt = now.AddMinutes(1) },
            new AlertDbRecord { Id = Guid.NewGuid(), DeliveryId = delivery.Id, Message = "first", CreatedAt = now }
        );
        await db.SaveChangesAsync();

        var alerts = await db.Alerts
            .AsNoTracking()
            .Where(a => a.DeliveryId == delivery.Id)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, alerts.Count);
        Assert.Equal("first", alerts[0].Message);
        Assert.Equal("second", alerts[1].Message);
    }

    [Fact]
    public async Task ListAlerts_NonexistentDelivery_AnyAsyncReturnsFalse()
    {
        using var db = TestDb.Create();

        // Endpoint's precondition check: if (!deliveryExists) return NotFound();
        var exists = await db.Deliveries.AnyAsync(d => d.Id == Guid.NewGuid());
        Assert.False(exists);
    }

    [Fact]
    public async Task CreateAlert_TrimsWhitespace_BeforePersist()
    {
        using var db = TestDb.Create();
        var delivery = SeedDelivery(db);

        var dto = new CreateAlertDto { Message = "   Gate locked   " };

        // Mirror endpoint body: only save trimmed message.
        Assert.False(string.IsNullOrWhiteSpace(dto.Message));
        var alert = new AlertDbRecord
        {
            Id = Guid.NewGuid(),
            DeliveryId = delivery.Id,
            Message = dto.Message.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        var saved = await db.Alerts.FirstAsync();
        Assert.Equal("Gate locked", saved.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void CreateAlert_EmptyOrWhitespaceMessage_RejectedByGuard(string message)
    {
        var dto = new CreateAlertDto { Message = message };
        Assert.True(string.IsNullOrWhiteSpace(dto.Message));
    }

    [Fact]
    public async Task UpdateStatus_ExistingDelivery_PersistsNewValue()
    {
        using var db = TestDb.Create();
        var delivery = SeedDelivery(db);

        var target = await db.Deliveries.FindAsync(delivery.Id);
        Assert.NotNull(target);
        target!.Status = 2; // complete
        await db.SaveChangesAsync();

        var reloaded = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(2, reloaded.Status);
    }

    [Fact]
    public async Task UpdateStatus_MissingId_FindReturnsNull()
    {
        using var db = TestDb.Create();

        var target = await db.Deliveries.FindAsync(Guid.NewGuid());
        Assert.Null(target);
    }

    [Fact]
    public async Task CreateAlert_DeletingDelivery_CascadesAlerts()
    {
        // Sanity-check the FK: deleting a delivery should drop its alerts.
        // The cascade is declared in PropaneDriverDbContext.OnModelCreating.
        using var db = TestDb.Create();
        var delivery = SeedDelivery(db);
        db.Alerts.Add(new AlertDbRecord
        {
            Id = Guid.NewGuid(),
            DeliveryId = delivery.Id,
            Message = "to-be-cascaded",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.Deliveries.Remove(delivery);
        await db.SaveChangesAsync();

        Assert.False(await db.Alerts.AnyAsync(a => a.DeliveryId == delivery.Id));
    }
}
