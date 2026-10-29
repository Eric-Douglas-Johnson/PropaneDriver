using System.Linq.Expressions;
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

            // Return the signed-in driver's open fuel log, in saved order
            group.MapGet("", async (
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                var driverId = user.GetUserId();
                if (driverId is null) return Results.Forbid();

                try
                {
                    return Results.Ok(await LoadOpenLogEntriesAsync(db, driverId.Value));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load fuel log for driver {DriverId}", driverId);
                    return Results.Problem(detail: ex.Message, title: "Failed to load fuel log", statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            // Replace the rows of the signed-in driver's open fuel log
            group.MapPut("", async (
                SaveFuelLogDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                var driverId = user.GetUserId();
                if (driverId is null) return Results.Forbid();

                try
                {
                    var recordedAt = DateTime.UtcNow;

                    var openLog = await db.FuelLogs.FirstOrDefaultAsync(IsOpenLogFor(driverId.Value));
                    if (openLog is null)
                    {
                        openLog = new FuelLogDbRecord
                        {
                            Id = Guid.NewGuid(),
                            DriverId = driverId.Value,
                            StartedAt = recordedAt
                        };
                        db.FuelLogs.Add(openLog);
                    }
                    else
                    {
                        var existingEntries = await db.FuelLogEntries
                            .Where(entry => entry.FuelLogId == openLog.Id)
                            .ToListAsync();
                        db.FuelLogEntries.RemoveRange(existingEntries);
                    }

                    var newEntries = (dto.Entries ?? new List<FuelLogEntryDto>())
                        // Skip fully-blank rows so an empty trailing row the
                        // driver never filled in doesn't persist.
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.EquipmentNumber)
                                        || entry.MeterValue != 0
                                        || entry.GallonsPumped != 0)
                        .Select((entry, index) => new FuelLogEntryDbRecord
                        {
                            Id = Guid.NewGuid(),
                            FuelLogId = openLog.Id,
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

            // Complete the signed-in driver's open log
            group.MapPost("start-new", async (
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                var driverId = user.GetUserId();
                if (driverId is null) return Results.Forbid();

                try
                {
                    var openLog = await db.FuelLogs.FirstOrDefaultAsync(IsOpenLogFor(driverId.Value));
                    if (openLog is null) return Results.Ok();

                    var hasFillRows = await db.FuelLogEntries
                        .AnyAsync(entry => entry.FuelLogId == openLog.Id && entry.SortOrder > 0);

                    if (hasFillRows)
                    {
                        openLog.CompletedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        db.FuelLogEntries.RemoveRange(await db.FuelLogEntries
                            .Where(entry => entry.FuelLogId == openLog.Id)
                            .ToListAsync());
                        db.FuelLogs.Remove(openLog);
                    }

                    await db.SaveChangesAsync();
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to start a new fuel log for driver {DriverId}", driverId);
                    return Results.Problem(detail: ex.Message, title: "Failed to start a new fuel log", statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            // Get the signed-in driver's completed logs, most recent first.
            group.MapGet("history", async (
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                var driverId = user.GetUserId();
                if (driverId is null) return Results.Forbid();

                try
                {
                    var completedLogs = db.FuelLogs
                        .Where(log => log.DriverId == driverId.Value && log.CompletedAt != null);
                    return Results.Ok(await SummarizeLogsAsync(db, completedLogs));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load fuel log history for driver {DriverId}", driverId);
                    return Results.Problem(detail: ex.Message, title: "Failed to load fuel log history", statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            // Supervisor/admin: every driver's logs for review on the Admin page
            group.MapGet("all", async (
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    var logsWithEntries = db.FuelLogs.Where(log =>
                        log.CompletedAt != null
                        || db.FuelLogEntries.Any(entry => entry.FuelLogId == log.Id && entry.SortOrder > 0));
                    return Results.Ok(await SummarizeLogsAsync(db, logsWithEntries));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load all fuel logs");
                    return Results.Problem(detail: ex.Message, title: "Failed to load fuel logs", statusCode: 500);
                }
            }).RequireAuthorization("SupervisorOrAdmin");

            // One log's rows, open or completed
            group.MapGet("logs/{fuelLogId:guid}", async (
                Guid fuelLogId,
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    var fuelLog = await db.FuelLogs
                        .AsNoTracking()
                        .FirstOrDefaultAsync(log => log.Id == fuelLogId);
                    if (fuelLog is null) return Results.NotFound();
                    if (!user.CanAccessDriverData(fuelLog.DriverId)) return Results.Forbid();

                    return Results.Ok(await LoadEntriesAsync(
                        db.FuelLogEntries.Where(entry => entry.FuelLogId == fuelLogId)));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load fuel log {FuelLogId}", fuelLogId);
                    return Results.Problem(detail: ex.Message, title: "Failed to load fuel log", statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            group.MapGet("{driverId:guid}", async (
                Guid driverId,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    return Results.Ok(await LoadOpenLogEntriesAsync(db, driverId));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load fuel log for driver {DriverId}", driverId);
                    return Results.Problem(detail: ex.Message, title: "Failed to load fuel log", statusCode: 500);
                }
            }).RequireAuthorization("AdminOnly");

            group.MapDelete("{driverId:guid}", async (
                Guid driverId,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    var openLog = await db.FuelLogs.FirstOrDefaultAsync(IsOpenLogFor(driverId));
                    if (openLog is null) return Results.NotFound();

                    var existingEntries = await db.FuelLogEntries
                        .Where(entry => entry.FuelLogId == openLog.Id)
                        .ToListAsync();

                    db.FuelLogEntries.RemoveRange(existingEntries);
                    db.FuelLogs.Remove(openLog);
                    await db.SaveChangesAsync();

                    return Results.Ok(new { Deleted = existingEntries.Count });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete fuel log for driver {DriverId}", driverId);
                    return Results.Problem(detail: ex.Message, title: "Failed to delete fuel log", statusCode: 500);
                }
            }).RequireAuthorization("AdminOnly");

            return app;
        }

        private static Expression<Func<FuelLogDbRecord, bool>> IsOpenLogFor(Guid driverId) =>
            log => log.DriverId == driverId && log.CompletedAt == null;

        private static Task<List<FuelLogEntryDto>> LoadOpenLogEntriesAsync(PropaneDriverDbContext db, Guid driverId)
        {
            var openLogIds = db.FuelLogs.Where(IsOpenLogFor(driverId)).Select(log => log.Id);
            return LoadEntriesAsync(db.FuelLogEntries.Where(entry => openLogIds.Contains(entry.FuelLogId)));
        }

        private static Task<List<FuelLogEntryDto>> LoadEntriesAsync(IQueryable<FuelLogEntryDbRecord> entries) =>
            entries
                .AsNoTracking()
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

        // Open logs first, then completed ones newest first. Row 0 is the Start
        // baseline, so it's left out of the fill count.
        private static Task<List<FuelLogSummaryDto>> SummarizeLogsAsync(
            PropaneDriverDbContext db,
            IQueryable<FuelLogDbRecord> logs) =>
            logs
                .AsNoTracking()
                .OrderBy(log => log.CompletedAt == null ? 0 : 1)
                .ThenByDescending(log => log.CompletedAt)
                .ThenByDescending(log => log.StartedAt)
                .Select(log => new FuelLogSummaryDto
                {
                    Id = log.Id.ToString(),
                    DriverId = log.DriverId.ToString(),
                    StartedAt = log.StartedAt,
                    CompletedAt = log.CompletedAt,
                    EntryCount = db.FuelLogEntries
                        .Count(entry => entry.FuelLogId == log.Id && entry.SortOrder > 0),
                    TotalGallons = db.FuelLogEntries
                        .Where(entry => entry.FuelLogId == log.Id && entry.GallonsPumped > 0)
                        .Sum(entry => entry.GallonsPumped)
                })
                .ToListAsync();
    }
}
