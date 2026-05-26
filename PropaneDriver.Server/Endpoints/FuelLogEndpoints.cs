using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Authorization;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class FuelLogEndpoints
    {
        public static IEndpointRouteBuilder MapFuelLogEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/fuel-log");

            // Return the signed-in driver's fuel log, in saved order. The
            // gallons-pumped figure is order-dependent (each row's gallons is
            // its meter minus the previous row's), so rows always come back
            // ordered by SortOrder.
            group.MapGet("", async (
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                var driverId = user.GetDriverId();
                if (driverId is null) return Results.Forbid();

                var entries = await db.FuelLogEntries
                    .AsNoTracking()
                    .Where(entry => entry.DriverId == driverId.Value)
                    .OrderBy(entry => entry.SortOrder)
                    .Select(entry => new FuelLogEntryDto
                    {
                        Id = entry.Id.ToString(),
                        EquipmentNumber = entry.EquipmentNumber,
                        MeterValue = entry.MeterValue,
                        GallonsPumped = entry.GallonsPumped,
                        SortOrder = entry.SortOrder,
                        RecordedAt = entry.RecordedAt
                    })
                    .ToListAsync();

                return Results.Ok(entries);
            }).RequireAuthorization("AuthenticatedDriver");

            // Replace the signed-in driver's entire fuel log with the posted
            // rows. The client edits the whole grid and saves in one shot, so
            // a full replace keeps server and client in lockstep without
            // per-row diffing. SortOrder is assigned from list position.
            group.MapPut("", async (
                SaveFuelLogDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                var driverId = user.GetDriverId();
                if (driverId is null) return Results.Forbid();

                try
                {
                    var existingEntries = await db.FuelLogEntries
                        .Where(entry => entry.DriverId == driverId.Value)
                        .ToListAsync();
                    db.FuelLogEntries.RemoveRange(existingEntries);

                    var recordedAt = DateTime.UtcNow;
                    var newEntries = (dto.Entries ?? new List<FuelLogEntryDto>())
                        // Skip fully-blank rows so an empty trailing row the
                        // driver never filled in doesn't persist.
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.EquipmentNumber)
                                        || entry.MeterValue != 0
                                        || entry.GallonsPumped != 0)
                        .Select((entry, index) => new FuelLogEntryDbRecord
                        {
                            Id = Guid.NewGuid(),
                            DriverId = driverId.Value,
                            EquipmentNumber = (entry.EquipmentNumber ?? string.Empty).Trim(),
                            MeterValue = entry.MeterValue,
                            GallonsPumped = entry.GallonsPumped,
                            SortOrder = index,
                            RecordedAt = recordedAt
                        })
                        .ToList();

                    db.FuelLogEntries.AddRange(newEntries);
                    await db.SaveChangesAsync();

                    return Results.Ok(new { Saved = newEntries.Count });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to save fuel log for driver {DriverId}", driverId);
                    return Results.Problem(detail: ex.Message, title: "Failed to save fuel log", statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            return app;
        }
    }
}
