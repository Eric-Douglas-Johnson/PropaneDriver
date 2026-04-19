using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Tests;

// Shared helper for producing an isolated in-memory DbContext per test.
// Mirrors the pattern already used by DeliveryTimesEndpointTests but gives
// every test file a single import point.
internal static class TestDb
{
    public static PropaneDriverDbContext Create()
    {
        var options = new DbContextOptionsBuilder<PropaneDriverDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PropaneDriverDbContext(options);
    }
}
