using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Authorization;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Server.Endpoints
{
    public static class AlertEndpoints
    {
        public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/alerts");

            // Delete an alert. Drivers can delete alerts on their own
            // deliveries (Dispatch); admins can delete any (Admin). The
            // alert → delivery → route → driver chain is what we walk to
            // check ownership.
            group.MapDelete("{id:guid}", async (
                Guid id,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id);
                if (alert is null) return Results.NotFound();

                // Walk alert → delivery → route by id to find the owning driver.
                var routeDriverId = await db.Deliveries
                    .Where(d => d.Id == alert.DeliveryId)
                    .Join(db.Routes, d => d.RouteId, r => r.Id, (d, r) => r.DriverId)
                    .FirstOrDefaultAsync();

                if (!user.CanAccessDriverData(routeDriverId))
                    return Results.Forbid();

                db.Alerts.Remove(alert);
                await db.SaveChangesAsync();
                return Results.Ok(new { Deleted = true, AlertId = id });
            }).RequireAuthorization("AuthenticatedDriver");

            // Mark an alert as seen (idempotent). Available to any
            // authenticated driver — but still ownership-guarded so a
            // driver can't dismiss someone else's alerts.
            group.MapPut("{id:guid}/seen", async (
                Guid id,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id);
                if (alert is null) return Results.NotFound();

                var routeDriverId = await db.Deliveries
                    .Where(d => d.Id == alert.DeliveryId)
                    .Join(db.Routes, d => d.RouteId, r => r.Id, (d, r) => r.DriverId)
                    .FirstOrDefaultAsync();

                if (!user.CanAccessDriverData(routeDriverId))
                    return Results.Forbid();

                if (!alert.Seen)
                {
                    alert.Seen = true;
                    await db.SaveChangesAsync();
                }

                return Results.Ok(new { alert.Id, alert.Seen });
            }).RequireAuthorization("AuthenticatedDriver");

            return app;
        }
    }
}
