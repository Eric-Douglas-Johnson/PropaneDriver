using PropaneDriver.Server.Data;

namespace PropaneDriver.Server.Endpoints
{
    public static class AlertEndpoints
    {
        public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/alerts");

            // Delete an alert
            group.MapDelete("{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
            {
                var alert = await db.Alerts.FindAsync(id);
                if (alert is null) return Results.NotFound();

                db.Alerts.Remove(alert);
                await db.SaveChangesAsync();
                return Results.Ok(new { Deleted = true, AlertId = id });
            });

            // Mark an alert as seen (idempotent)
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
            });

            return app;
        }
    }
}
