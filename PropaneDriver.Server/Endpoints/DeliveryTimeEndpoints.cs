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
                if (string.IsNullOrWhiteSpace(dto.Street) ||
                    string.IsNullOrWhiteSpace(dto.City) ||
                    string.IsNullOrWhiteSpace(dto.State) ||
                    string.IsNullOrWhiteSpace(dto.ZipCode))
                    return Results.BadRequest(new { Message = "All address fields (Street, City, State, ZipCode) are required." });

                try
                {
                    var entity = new DeliveryTimeEntity
                    {
                        DeliveryId = dto.DeliveryId,
                        Street = dto.Street.Trim(),
                        City = dto.City.Trim(),
                        State = dto.State.Trim(),
                        ZipCode = dto.ZipCode.Trim(),
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
                    logger.LogError(ex, "Failed to save delivery time for {Street}, {City}", dto.Street, dto.City);
                    return Results.Problem(
                        detail: ex.Message,
                        title: "Failed to save delivery time",
                        statusCode: 500);
                }
            });

            // Get average delivery time for an address. Drops shortest + longest
            // samples once there's enough data to make outlier-trimming meaningful.
            group.MapGet("average", async (string street, string city, string state, string zip, PropaneDriverDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(city) ||
                    string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(zip))
                    return Results.BadRequest(new { Message = "street, city, state, and zip query parameters are required." });

                var s = street.Trim();
                var c = city.Trim();
                var st = state.Trim();
                var z = zip.Trim();

                var times = await db.DeliveryTimes
                    .Where(t => t.Street == s && t.City == c && t.State == st && t.ZipCode == z)
                    .Select(t => t.TimeIntervalSeconds)
                    .ToListAsync();

                if (times.Count == 0)
                    return Results.Ok(new { Street = s, City = c, State = st, ZipCode = z, AverageSeconds = 0.0, Count = 0 });

                times.Sort();

                // Once we have at least 5 data points, remove the shortest and longest times
                // in order to remove outliers
                if (times.Count > 4)
                {
                    times.RemoveAt(times.Count - 1);
                    times.RemoveAt(0);
                }

                var avg = times.Average();
                return Results.Ok(new { Street = s, City = c, State = st, ZipCode = z, AverageSeconds = avg, Count = times.Count });
            });

            return app;
        }
    }
}
