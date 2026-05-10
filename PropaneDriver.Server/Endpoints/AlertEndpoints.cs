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
                var alertWithRoute = await db.Alerts
                    .Where(a => a.Id == id)
                    .Select(a => new { Alert = a, RouteDriverId = a.Delivery!.Route!.DriverId })
                    .FirstOrDefaultAsync();

                if (alertWithRoute is null) return Results.NotFound();

                if (!user.CanAccessDriverData(alertWithRoute.RouteDriverId))
                    return Results.Forbid();

                db.Alerts.Remove(alertWithRoute.Alert);
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
                var alertWithRoute = await db.Alerts
                    .Where(a => a.Id == id)
                    .Select(a => new { Alert = a, RouteDriverId = a.Delivery!.Route!.DriverId })
                    .FirstOrDefaultAsync();

                if (alertWithRoute is null) return Results.NotFound();

                if (!user.CanAccessDriverData(alertWithRoute.RouteDriverId))
                    return Results.Forbid();

                if (!alertWithRoute.Alert.Seen)
                {
                    alertWithRoute.Alert.Seen = true;
                    await db.SaveChangesAsync();
                }

                return Results.Ok(new { alertWithRoute.Alert.Id, alertWithRoute.Alert.Seen });
            }).RequireAuthorization("AuthenticatedDriver");

            return app;
        }
    }
}
