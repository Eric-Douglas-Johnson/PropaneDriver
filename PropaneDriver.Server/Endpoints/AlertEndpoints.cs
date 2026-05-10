using PropaneDriver.Server.Data;

namespace PropaneDriver.Server.Endpoints
{
    public static class AlertEndpoints
    {
        public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/alerts");

            // Delete an alert — admin-only (called from the Admin page's
            // alert-management panel).
            group.MapDelete("{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
            {
                var alert = await db.Alerts.FindAsync(id);
                if (alert is null) return Results.NotFound();

                db.Alerts.Remove(alert);
                await db.SaveChangesAsync();
                return Results.Ok(new { Deleted = true, AlertId = id });
            }).RequireAuthorization("AdminOnly");

            // Mark an alert as seen (idempotent). Available to any authenticated
            // driver since the driver-side Navigation page dismisses alerts as
            // the route is run.
            group.MapPut("{id:guid}/seen", async (Guid id, PropaneDriverDbContext db) =>
            {
                var alert = await db.Alerts.FindAsync(id);
                if (alert is null) return Results.NotFound();

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
