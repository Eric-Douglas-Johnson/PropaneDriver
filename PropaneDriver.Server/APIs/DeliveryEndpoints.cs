using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Authorization;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class DeliveryEndpoints
    {
        public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/deliveries");

            // List alerts for a delivery — readable by any signed-in
            // driver who owns the delivery, plus admins.
            group.MapGet("{id:guid}/alerts", async (
                Guid id,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                // Owning driver comes from the delivery's route; join by id.
                // Nullable so a missing delivery (null) is distinct from a real id.
                var routeDriverId = await db.Deliveries
                    .Where(d => d.Id == id)
                    .Join(db.Routes, d => d.RouteId, r => r.Id, (d, r) => (Guid?)r.DriverId)
                    .FirstOrDefaultAsync();

                if (routeDriverId is null) return Results.NotFound();

                if (!user.CanAccessDriverData(routeDriverId.Value))
                    return Results.Forbid();

                var alerts = await db.Alerts
                    .AsNoTracking()
                    .Where(a => a.DeliveryId == id)
                    .OrderBy(a => a.CreatedAt)
                    .Select(a => new AlertDto
                    {
                        Id = a.Id.ToString(),
                        DeliveryId = a.DeliveryId.ToString(),
                        Message = a.Message,
                        CreatedAt = a.CreatedAt,
                        Seen = a.Seen
                    })
                    .ToListAsync();

                return Results.Ok(alerts);
            }).RequireAuthorization("AuthenticatedDriver");

            // Create an alert for a delivery. Drivers can add alerts to
            // their own deliveries (Dispatch); admins can add to any
            // (Admin). Ownership is checked through the route.
            group.MapPost("{id:guid}/alerts", async (
                Guid id,
                CreateAlertDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(dto.Message))
                    return Results.BadRequest(new { Message = "Alert message is required." });

                var routeDriverId = await db.Deliveries
                    .Where(d => d.Id == id)
                    .Join(db.Routes, d => d.RouteId, r => r.Id, (d, r) => (Guid?)r.DriverId)
                    .FirstOrDefaultAsync();

                if (routeDriverId is null) return Results.NotFound();

                if (!user.CanAccessDriverData(routeDriverId.Value))
                    return Results.Forbid();

                var alert = new AlertDbRecord
                {
                    Id = Guid.NewGuid(),
                    DeliveryId = id,
                    Message = dto.Message.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                db.Alerts.Add(alert);
                await db.SaveChangesAsync();

                return Results.Ok(new AlertDto
                {
                    Id = alert.Id.ToString(),
                    DeliveryId = alert.DeliveryId.ToString(),
                    Message = alert.Message,
                    CreatedAt = alert.CreatedAt
                });
            }).RequireAuthorization("AuthenticatedDriver");

            // Update a delivery's status — driver-side action when running
            // a route. Ownership-checked so a driver can't mark another
            // driver's delivery complete.
            group.MapPut("{id:guid}/status", async (
                Guid id,
                DeliveryStatusUpdateDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    // Fetch the delivery tracked (it's mutated below), then look
                    // up its owning driver via the route in a separate query.
                    var delivery = await db.Deliveries
                        .FirstOrDefaultAsync(d => d.Id == id);
                    if (delivery is null) return Results.NotFound();

                    var routeDriverId = await db.Routes
                        .Where(r => r.Id == delivery.RouteId)
                        .Select(r => r.DriverId)
                        .FirstOrDefaultAsync();

                    if (!user.CanAccessDriverData(routeDriverId))
                        return Results.Forbid();

                    delivery.Status = dto.Status;
                    await db.SaveChangesAsync();
                    return Results.Ok(new { delivery.Id, delivery.Status });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update status for delivery {Id}", id);
                    return Results.Problem(detail: ex.Message, title: "Failed to update delivery status", statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            // Toggle the LongRunning flag on a single delivery. When true, the
            // driver client uses manual Start/Stop buttons instead of the
            // GPS-geofence auto-timer for this stop. Admin-only (per-row toggle
            // on the Admin page); ownership is still validated through the route.
            group.MapPut("{id:guid}/long-running", async (
                Guid id,
                DeliveryLongRunningUpdateDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    var delivery = await db.Deliveries
                        .FirstOrDefaultAsync(d => d.Id == id);
                    if (delivery is null) return Results.NotFound();

                    var routeDriverId = await db.Routes
                        .Where(r => r.Id == delivery.RouteId)
                        .Select(r => r.DriverId)
                        .FirstOrDefaultAsync();

                    if (!user.CanAccessDriverData(routeDriverId))
                        return Results.Forbid();

                    delivery.LongRunning = dto.LongRunning;
                    await db.SaveChangesAsync();
                    logger.LogInformation(
                        "Updated LongRunning for Delivery {Id} to {LongRunning}", id, dto.LongRunning);
                    return Results.Ok(new { delivery.Id, delivery.LongRunning });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update long-running for delivery {Id}", id);
                    return Results.Problem(detail: ex.Message, title: "Failed to update delivery long-running", statusCode: 500);
                }
            }).RequireAuthorization("AdminOnly");

            return app;
        }
    }
}
