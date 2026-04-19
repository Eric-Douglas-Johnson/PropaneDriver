using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class DeliveryTimeEndpoints
    {
        public static IEndpointRouteBuilder MapDeliveryTimeEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/delivery-times");

            // Store a delivery time record
            group.MapPost("", async (
                DeliveryTimeDto dto,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    var entity = new DeliveryTimeEntity
                    {
                        DeliveryId = dto.DeliveryId,
                        Address = dto.Address,
                        Latitude = dto.Latitude,
                        Longitude = dto.Longitude,
                        TimeIntervalSeconds = dto.TimeIntervalSeconds,
                        RecordedAt = DateTime.UtcNow
                    };

                    db.DeliveryTimes.Add(entity);
                    await db.SaveChangesAsync();

                    logger.LogInformation("Saved delivery time Id={Id}", entity.Id);
                    return Results.Ok(new { entity.Id, entity.RecordedAt });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to save delivery time for {Address}", dto.Address);
                    return Results.Problem(
                        detail: ex.Message,
                        title: "Failed to save delivery time",
                        statusCode: 500);
                }
            });

            // Get average delivery time for an address. Drops shortest + longest
            // samples once there's enough data to make outlier-trimming meaningful.
            group.MapGet("average/{address}", async (string address, PropaneDriverDbContext db) =>
            {
                var decodedAddress = Uri.UnescapeDataString(address);

                var times = await db.DeliveryTimes
                    .Where(t => t.Address == decodedAddress)
                    .Select(t => t.TimeIntervalSeconds)
                    .ToListAsync();

                if (times.Count == 0)
                    return Results.Ok(new { Address = decodedAddress, AverageSeconds = 0.0, Count = 0 });

                times.Sort();

                // Once we have at least 5 data points, remove the shortest and longest times
                // in order to remove outliers
                if (times.Count > 4)
                {
                    times.RemoveAt(times.Count - 1);
                    times.RemoveAt(0);
                }

                var avg = times.Average();
                return Results.Ok(new { Address = decodedAddress, AverageSeconds = avg, Count = times.Count });
            });

            return app;
        }
    }
}
