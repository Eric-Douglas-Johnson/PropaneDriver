using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class DeliveryEndpoints
    {
        public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/deliveries");

            // List alerts for a delivery
            group.MapGet("{id:guid}/alerts", async (Guid id, PropaneDriverDbContext db) =>
            {
                var deliveryExists = await db.Deliveries.AnyAsync(d => d.Id == id);
                if (!deliveryExists) return Results.NotFound();

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
            });

            // Create an alert for a delivery
            group.MapPost("{id:guid}/alerts", async (Guid id, CreateAlertDto dto, PropaneDriverDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(dto.Message))
                    return Results.BadRequest(new { Message = "Alert message is required." });

                var deliveryExists = await db.Deliveries.AnyAsync(d => d.Id == id);
                if (!deliveryExists) return Results.NotFound();

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
            });

            // Update a delivery's status
            group.MapPut("{id:guid}/status", async (
                Guid id,
                DeliveryStatusUpdateDto dto,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    var delivery = await db.Deliveries.FindAsync(id);
                    if (delivery is null)
                        return Results.NotFound();

                    delivery.Status = dto.Status;
                    await db.SaveChangesAsync();
                    return Results.Ok(new { delivery.Id, delivery.Status });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update status for delivery {Id}", id);
                    return Results.Problem(detail: ex.Message, title: "Failed to update delivery status", statusCode: 500);
                }
            });

            return app;
        }
    }
}
