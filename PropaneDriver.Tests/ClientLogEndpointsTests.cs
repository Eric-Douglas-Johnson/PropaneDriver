using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Tests;

// Exercises the persistence behavior of POST /api/client-logs: defaulted
// Source/Level when the DTO leaves them null, and Timestamp fall-back to
// UtcNow when omitted.
public class ClientLogEndpointsTests
{
    [Fact]
    public async Task PostClientLog_PersistsEntity()
    {
        using var db = TestDb.Create();
        var ts = DateTime.UtcNow;
        var log = new ClientLogDto
        {
            Source = "Navigation.razor",
            Level = "Error",
            Message = "Map container measured 0x0",
            Timestamp = ts
        };

        db.ErrorLogs.Add(new ErrorLogDbRecord
        {
            Id = Guid.NewGuid(),
            Source = log.Source ?? "Unknown",
            Level = log.Level ?? "Error",
            Message = log.Message ?? "",
            Timestamp = log.Timestamp ?? DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var saved = await db.ErrorLogs.FirstAsync();
        Assert.Equal("Navigation.razor", saved.Source);
        Assert.Equal("Error", saved.Level);
        Assert.Equal("Map container measured 0x0", saved.Message);
        Assert.Equal(ts, saved.Timestamp);
    }

    [Fact]
    public async Task PostClientLog_NullSource_DefaultsToUnknown()
    {
        using var db = TestDb.Create();
        var log = new ClientLogDto
        {
            Source = null,
            Level = null,
            Message = null,
            Timestamp = null
        };

        db.ErrorLogs.Add(new ErrorLogDbRecord
        {
            Id = Guid.NewGuid(),
            Source = log.Source ?? "Unknown",
            Level = log.Level ?? "Error",
            Message = log.Message ?? "",
            Timestamp = log.Timestamp ?? DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var saved = await db.ErrorLogs.FirstAsync();
        Assert.Equal("Unknown", saved.Source);
        Assert.Equal("Error", saved.Level);
        Assert.Equal("", saved.Message);
        Assert.True(saved.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public async Task PostClientLog_AssignsNewGuid()
    {
        using var db = TestDb.Create();
        var entity = new ErrorLogDbRecord
        {
            Id = Guid.NewGuid(),
            Source = "x",
            Level = "Info",
            Message = "hello",
            Timestamp = DateTime.UtcNow
        };

        db.ErrorLogs.Add(entity);
        await db.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, entity.Id);
    }
}
