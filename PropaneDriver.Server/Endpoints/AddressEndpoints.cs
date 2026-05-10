using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class AddressEndpoints
    {
        public static IEndpointRouteBuilder MapAddressEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/addresses");

            // Fetch a single Address row as a GeoAddressDto. Used by the
            // Navigation page to refresh its cached query-string state on
            // init, so flags toggled elsewhere (Admin page, prior nav
            // session) reflect their current server value instead of the
            // stale value baked into the URL.
            group.MapGet("{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
            {
                var address = await db.Addresses
                    .AsNoTracking()
                    .Where(a => a.Id == id)
                    .Select(a => new GeoAddressDto
                    {
                        Id = a.Id,
                        Street = a.Street,
                        City = a.City,
                        State = a.State,
                        ZipCode = a.ZipCode,
                        Latitude = a.Latitude,
                        Longitude = a.Longitude,
                        AvgDeliveryTimeSeconds = a.AvgDeliveryTimeSeconds,
                        TankLocation = a.TankLocation,
                        BackIn = a.BackIn,
                        LongRunning = a.LongRunning
                    })
                    .FirstOrDefaultAsync();

                return address is null
                    ? Results.NotFound(new { Message = $"Address {id} not found." })
                    : Results.Ok(address);
            }).RequireAuthorization("AuthenticatedDriver");

            // Overwrite the stored coordinates on an Address row. Used by the
            // driver-side "set pin here" button when the geocoded location is
            // off (common for rural long driveways where Google drops the pin
            // at the road entrance instead of the tank). Leaves every other
            // field on the row untouched.
            group.MapPut("{id:guid}/coordinates", async (
                Guid id,
                AddressCoordinatesUpdateDto dto,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                // Sanity-check the payload. (0,0) is the canonical "unset"
                // sentinel our code uses elsewhere, and absurd values almost
                // always indicate a broken GPS frame rather than a real fix.
                if (dto.Latitude == 0 && dto.Longitude == 0)
                    return Results.BadRequest(new { Message = "Latitude and Longitude cannot both be 0." });
                if (dto.Latitude < -90 || dto.Latitude > 90)
                    return Results.BadRequest(new { Message = "Latitude must be between -90 and 90." });
                if (dto.Longitude < -180 || dto.Longitude > 180)
                    return Results.BadRequest(new { Message = "Longitude must be between -180 and 180." });

                var address = await db.Addresses.FindAsync(id);
                if (address is null)
                    return Results.NotFound(new { Message = $"Address {id} not found." });

                address.Latitude = dto.Latitude;
                address.Longitude = dto.Longitude;

                try
                {
                    await db.SaveChangesAsync();
                    logger.LogInformation(
                        "Updated coordinates for Address {AddressId} to ({Lat},{Lng})",
                        id, dto.Latitude, dto.Longitude);

                    return Results.Ok(new { id, address.Latitude, address.Longitude });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update coordinates for Address {AddressId}", id);
                    return Results.Problem(
                        detail: ex.Message,
                        title: "Failed to update coordinates",
                        statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            // Overwrite the TankLocation note on an Address row. Called
            // from the Navigation page when a driver taps the "Add tank
            // location" button for a delivery whose address doesn't yet
            // have one. Null or whitespace clears the field (so this is
            // also the path for fixing a mistyped note).
            group.MapPut("{id:guid}/tank-location", async (
                Guid id,
                AddressTankLocationUpdateDto dto,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                var address = await db.Addresses.FindAsync(id);
                if (address is null)
                    return Results.NotFound(new { Message = $"Address {id} not found." });

                // Normalize: treat whitespace-only as "clear it". Trim
                // so we don't store leading/trailing spaces from a
                // copy-paste.
                address.TankLocation = string.IsNullOrWhiteSpace(dto.TankLocation)
                    ? null
                    : dto.TankLocation.Trim();

                try
                {
                    await db.SaveChangesAsync();
                    logger.LogInformation(
                        "Updated TankLocation for Address {AddressId} (length={Length})",
                        id, address.TankLocation?.Length ?? 0);

                    return Results.Ok(new { id, address.TankLocation });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update TankLocation for Address {AddressId}", id);
                    return Results.Problem(
                        detail: ex.Message,
                        title: "Failed to update tank location",
                        statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            // Toggle the BackIn flag on an Address row. Used by the
            // admin/driver UI to mark driveways where the truck must
            // back in (vs. pulling forward and turning around).
            group.MapPut("{id:guid}/back-in", async (
                Guid id,
                AddressBackInUpdateDto dto,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                var address = await db.Addresses.FindAsync(id);
                if (address is null)
                    return Results.NotFound(new { Message = $"Address {id} not found." });

                address.BackIn = dto.BackIn;

                try
                {
                    await db.SaveChangesAsync();
                    logger.LogInformation(
                        "Updated BackIn for Address {AddressId} to {BackIn}",
                        id, dto.BackIn);

                    return Results.Ok(new { id, address.BackIn });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update BackIn for Address {AddressId}", id);
                    return Results.Problem(
                        detail: ex.Message,
                        title: "Failed to update back-in flag",
                        statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            // Toggle the LongRunning flag on an Address row. When true,
            // the driver client uses Start/Stop buttons instead of the
            // GPS-geofence auto-timer for deliveries to this address.
            group.MapPut("{id:guid}/long-running", async (
                Guid id,
                AddressLongRunningUpdateDto dto,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                var address = await db.Addresses.FindAsync(id);
                if (address is null)
                    return Results.NotFound(new { Message = $"Address {id} not found." });

                address.LongRunning = dto.LongRunning;

                try
                {
                    await db.SaveChangesAsync();
                    logger.LogInformation(
                        "Updated LongRunning for Address {AddressId} to {LongRunning}",
                        id, dto.LongRunning);

                    return Results.Ok(new { id, address.LongRunning });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update LongRunning for Address {AddressId}", id);
                    return Results.Problem(
                        detail: ex.Message,
                        title: "Failed to update long-running flag",
                        statusCode: 500);
                }
            }).RequireAuthorization("AdminOnly");

            return app;
        }
    }
}
