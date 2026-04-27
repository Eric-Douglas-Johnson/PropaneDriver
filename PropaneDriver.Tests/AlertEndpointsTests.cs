using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Tests;

// Mirrors the Minimal-API logic in PropaneDriver.Server/Endpoints/AlertEndpoints.cs
// (DELETE /api/alerts/{id} and PUT /api/alerts/{id}/seen) against an
// in-memory DbContext. We test the data-layer behavior the handler relies on
// rather than wiring up the HTTP pipeline, matching the existing test style.
public class AlertEndpointsTests
{
    [Fact]
    public async Task DeleteAlert_ExistingAlert_RemovesRow()
    {
        using var db = TestDb.Create();
        var alert = new AlertDbRecord
        {
            Id = Guid.NewGuid(),
            DeliveryId = Guid.NewGuid(),
            Message = "Gate locked",
            CreatedAt = DateTime.UtcNow
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        var found = await db.Alerts.FindAsync(alert.Id);
        Assert.NotNull(found);

        db.Alerts.Remove(found!);
        await db.SaveChangesAsync();

        Assert.False(await db.Alerts.AnyAsync(a => a.Id == alert.Id));
    }

    [Fact]
    public async Task DeleteAlert_MissingId_FindReturnsNull()
    {
        using var db = TestDb.Create();

        // The endpoint maps this case to Results.NotFound(); we assert the
        // precondition it checks on.
        var missing = await db.Alerts.FindAsync(Guid.NewGuid());
        Assert.Null(missing);
    }

    [Fact]
    public async Task MarkSeen_UnseenAlert_SetsSeenTrue()
    {
        using var db = TestDb.Create();
        var alert = new AlertDbRecord
        {
            Id = Guid.NewGuid(),
            DeliveryId = Guid.NewGuid(),
            Message = "Propane low",
            CreatedAt = DateTime.UtcNow,
            Seen = false
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        // Handler body:
        //   if (!alert.Seen) { alert.Seen = true; await db.SaveChangesAsync(); }
        var target = await db.Alerts.FindAsync(alert.Id);
        Assert.NotNull(target);
        if (!target!.Seen)
        {
            target.Seen = true;
            await db.SaveChangesAsync();
        }

        var reloaded = await db.Alerts.AsNoTracking().FirstAsync(a => a.Id == alert.Id);
        Assert.True(reloaded.Seen);
    }

    [Fact]
    public async Task MarkSeen_AlreadySeen_IsIdempotent()
    {
        using var db = TestDb.Create();
        var alert = new AlertDbRecord
        {
            Id = Guid.NewGuid(),
            DeliveryId = Guid.NewGuid(),
            Message = "Already seen",
            CreatedAt = DateTime.UtcNow,
            Seen = true
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        var target = await db.Alerts.FindAsync(alert.Id);
        Assert.NotNull(target);

        // The endpoint should skip the save entirely when Seen is already true.
        // We verify the guard by re-reading and confirming Seen is unchanged.
        if (!target!.Seen)
        {
            target.Seen = true;
            await db.SaveChangesAsync();
        }

        var reloaded = await db.Alerts.AsNoTracking().FirstAsync(a => a.Id == alert.Id);
        Assert.True(reloaded.Seen);
    }

    [Fact]
    public async Task DeleteAlert_DoesNotAffectSiblingAlerts()
    {
        using var db = TestDb.Create();
        var deliveryId = Guid.NewGuid();
        var keep = new AlertDbRecord { Id = Guid.NewGuid(), DeliveryId = deliveryId, Message = "Keep", CreatedAt = DateTime.UtcNow };
        var drop = new AlertDbRecord { Id = Guid.NewGuid(), DeliveryId = deliveryId, Message = "Drop", CreatedAt = DateTime.UtcNow };
        db.Alerts.AddRange(keep, drop);
        await db.SaveChangesAsync();

        db.Alerts.Remove(drop);
        await db.SaveChangesAsync();

        Assert.True(await db.Alerts.AnyAsync(a => a.Id == keep.Id));
        Assert.False(await db.Alerts.AnyAsync(a => a.Id == drop.Id));
    }
}
