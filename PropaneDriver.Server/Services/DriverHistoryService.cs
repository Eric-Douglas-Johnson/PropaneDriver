using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Server.Services
{
    // Owns what a driver accumulates (routes, whose deliveries and alerts cascade,
    // and fuel log entries) so account code never reaches into those tables.
    // Removals are staged on the scoped DbContext; the caller saves.
    public class DriverHistoryService
    {
        private readonly PropaneDriverDbContext _db;

        public DriverHistoryService(PropaneDriverDbContext db)
        {
            _db = db;
        }

        public async Task<bool> HasHistoryAsync(Guid driverId) =>
            await _db.Routes.AnyAsync(r => r.DriverId == driverId)
            || await _db.FuelLogEntries.AnyAsync(f => f.DriverId == driverId);

        public async Task RemoveHistoryAsync(Guid driverId)
        {
            _db.Routes.RemoveRange(await _db.Routes.Where(r => r.DriverId == driverId).ToListAsync());
            _db.FuelLogEntries.RemoveRange(await _db.FuelLogEntries.Where(f => f.DriverId == driverId).ToListAsync());
        }
    }
}
