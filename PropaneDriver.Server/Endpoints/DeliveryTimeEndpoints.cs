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

            // Store a delivery time record and refresh the address average.
            group.MapPost("", async (
                DeliveryTimeDto dto,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                if (dto.AddressId == Guid.Empty)
                    return Results.BadRequest(new { Message = "AddressId is required." });

                var address = await db.Addresses.FindAsync(dto.AddressId);
                if (address is null)
                    return Results.BadRequest(new { Message = $"Address {dto.AddressId} not found." });

                try
                {
                    var entity = new DeliveryTimeDbRecord
                    {
                        DeliveryId = dto.DeliveryId,
                        AddressId = dto.AddressId,
                        TimeIntervalSeconds = dto.TimeIntervalSeconds,
                        RecordedAt = DateTime.UtcNow
                    };

                    db.DeliveryTimes.Add(entity);
                    await db.SaveChangesAsync();

                    // Recompute the stored average for this address.
                    var times = await db.DeliveryTimes
                        .Where(t => t.AddressId == dto.AddressId)
                        .Select(t => t.TimeIntervalSeconds)
                        .ToListAsync();

                    times.Sort();
                    if (times.Count > 4)
                    {
                        times.RemoveAt(times.Count - 1);
                        times.RemoveAt(0);
                    }

                    address.AvgDeliveryTimeSeconds = times.Count > 0 ? times.Average() : 0;
                    await db.SaveChangesAsync();

                    logger.LogInformation("Saved delivery time Id={Id} for Address={AddressId}", entity.Id, dto.AddressId);
                    return Results.Ok(new { entity.Id, entity.RecordedAt });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to save delivery time for AddressId={AddressId}", dto.AddressId);
                    return Results.Problem(
                        detail: ex.Message,
                        title: "Failed to save delivery time",
                        statusCode: 500);
                }
            });

            // Get average delivery time for an address by its ID.
            group.MapGet("average", async (Guid addressId, PropaneDriverDbContext db) =>
            {
                if (addressId == Guid.Empty)
                    return Results.BadRequest(new { Message = "addressId is required." });

                var address = await db.Addresses.FindAsync(addressId);
                if (address is null)
                    return Results.NotFound(new { Message = $"Address {addressId} not found." });

                return Results.Ok(new
                {
                    AddressId = addressId,
                    address.AvgDeliveryTimeSeconds,
                    address.Street,
                    address.City,
                    address.State,
                    address.ZipCode
                });
            });

            return app;
        }
    }
}
