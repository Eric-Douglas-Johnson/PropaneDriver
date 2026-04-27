using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;
using PropaneDriver.Server.Services;

namespace PropaneDriver.Server.Endpoints
{
    public static class RouteEndpoints
    {
        public static IEndpointRouteBuilder MapRouteEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/routes");

            // Get a route (with deliveries + alerts) for a driver on a specific date
            group.MapGet("{driverId:guid}/{date}", async (Guid driverId, DateOnly date, PropaneDriverDbContext db) =>
            {
                var route = await db.Routes
                    .AsNoTracking()
                    .Where(r => r.DriverId == driverId && r.Date == date)
                    .Select(r => new RouteDto
                    {
                        Id = r.Id.ToString(),
                        DriverId = r.DriverId.ToString(),
                        Date = r.Date,
                        EstimatedRouteTime = r.EstimatedRouteTime,
                        Deliveries = r.Deliveries
                            .OrderBy(d => d.SortOrder)
                            .Select(d => new DeliveryDto
                            {
                                Id = d.Id.ToString(),
                                CustomerName = d.CustomerName,
                                Date = r.Date,
                                Location = new GeoAddressDto
                                {
                                    Id = d.Address!.Id,
                                    Street = d.Address.Street,
                                    City = d.Address.City,
                                    State = d.Address.State,
                                    ZipCode = d.Address.ZipCode,
                                    Latitude = d.Address.Latitude,
                                    Longitude = d.Address.Longitude,
                                    AvgDeliveryTimeSeconds = d.Address.AvgDeliveryTimeSeconds,
                                    TankLocation = d.Address.TankLocation
                                },
                                AvgDeliveryTimeMinutes = d.AvgDeliveryTimeMinutes,
                                Status = d.Status,
                                Alerts = d.Alerts
                                    .OrderBy(a => a.CreatedAt)
                                    .Select(a => new AlertDto
                                    {
                                        Id = a.Id.ToString(),
                                        DeliveryId = a.DeliveryId.ToString(),
                                        Message = a.Message,
                                        CreatedAt = a.CreatedAt,
                                        Seen = a.Seen
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .FirstOrDefaultAsync();

                return route is null ? Results.NotFound() : Results.Ok(route);
            });

            // List all routes for a driver (summary info)
            group.MapGet("driver/{driverId:guid}", async (Guid driverId, PropaneDriverDbContext db) =>
            {
                var routes = await db.Routes
                    .AsNoTracking()
                    .Where(r => r.DriverId == driverId)
                    .OrderByDescending(r => r.Date)
                    .Select(r => new RouteListItemDto
                    {
                        Id = r.Id.ToString(),
                        Date = r.Date,
                        DeliveryCount = r.Deliveries.Count(),
                        CompletedCount = r.Deliveries.Count(d => d.Status == 2)
                    })
                    .ToListAsync();

                return Results.Ok(routes);
            });

            // Delete a route and its deliveries
            group.MapDelete("{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
            {
                var route = await db.Routes.FindAsync(id);
                if (route is null) return Results.NotFound();

                db.Routes.Remove(route); // cascade deletes deliveries
                await db.SaveChangesAsync();
                return Results.Ok(new { Deleted = true, RouteId = id });
            });

            // Get today's route (with deliveries + alerts) for a driver.
            // "Today" is computed in Central time because that's the driver's
            // local timezone and it's also what the Admin page uses when
            // saving Route.Date (DateTime.Today in the browser). Azure App
            // Service has no timezone set, so a bare DateTime.Today here
            // evaluates to UTC and silently misses the route for ~5 hours
            // every evening around the UTC day rollover.
            group.MapGet("today/{driverId:guid}", async (Guid driverId, PropaneDriverDbContext db) =>
            {
                var centralTz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
                var today = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, centralTz));
                var route = await db.Routes
                    .AsNoTracking()
                    .Where(r => r.DriverId == driverId && r.Date == today)
                    .Select(r => new RouteDto
                    {
                        Id = r.Id.ToString(),
                        DriverId = r.DriverId.ToString(),
                        Date = r.Date,
                        EstimatedRouteTime = r.EstimatedRouteTime,
                        Deliveries = r.Deliveries
                            .OrderBy(d => d.SortOrder)
                            .Select(d => new DeliveryDto
                            {
                                Id = d.Id.ToString(),
                                CustomerName = d.CustomerName,
                                Date = r.Date,
                                Location = new GeoAddressDto
                                {
                                    Id = d.Address!.Id,
                                    Street = d.Address.Street,
                                    City = d.Address.City,
                                    State = d.Address.State,
                                    ZipCode = d.Address.ZipCode,
                                    Latitude = d.Address.Latitude,
                                    Longitude = d.Address.Longitude,
                                    AvgDeliveryTimeSeconds = d.Address.AvgDeliveryTimeSeconds,
                                    TankLocation = d.Address.TankLocation
                                },
                                AvgDeliveryTimeMinutes = d.AvgDeliveryTimeMinutes,
                                Status = d.Status,
                                Alerts = d.Alerts
                                    .OrderBy(a => a.CreatedAt)
                                    .Select(a => new AlertDto
                                    {
                                        Id = a.Id.ToString(),
                                        DeliveryId = a.DeliveryId.ToString(),
                                        Message = a.Message,
                                        CreatedAt = a.CreatedAt,
                                        Seen = a.Seen
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .FirstOrDefaultAsync();

                if (route is null) return Results.NotFound();

                // Hydrate the actual recorded delivery time on each completed
                // stop so the minimized "Complete" row can show the duration
                // after a page reload, when the client-side _completedTimesSeconds
                // dictionary is empty. The DeliveryTimes table is keyed by the
                // delivery's string Id (Guid.ToString()), so we group by it and
                // pick the most recent record per delivery.
                var deliveryIds = route.Deliveries.Select(d => d.Id).ToList();
                if (deliveryIds.Count > 0)
                {
                    var times = await db.DeliveryTimes
                        .Where(t => deliveryIds.Contains(t.DeliveryId))
                        .Select(t => new { t.DeliveryId, t.TimeIntervalSeconds, t.RecordedAt })
                        .ToListAsync();

                    var latestByDelivery = times
                        .GroupBy(t => t.DeliveryId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.OrderByDescending(x => x.RecordedAt).First().TimeIntervalSeconds);

                    foreach (var d in route.Deliveries)
                    {
                        if (latestByDelivery.TryGetValue(d.Id, out var seconds))
                            d.RecordedTimeSeconds = seconds;
                    }
                }

                return Results.Ok(route);
            });

            // Create a route with deliveries
            group.MapPost("", async (CreateRouteDto dto, PropaneDriverDbContext db, ILogger<Program> logger) =>
            {
                if (!Guid.TryParse(dto.DriverId, out var driverId))
                    return Results.BadRequest(new { Message = "DriverId must be a valid GUID." });

                var invalidDelivery = dto.Deliveries.FirstOrDefault(d =>
                    string.IsNullOrWhiteSpace(d.Street) ||
                    string.IsNullOrWhiteSpace(d.City) ||
                    string.IsNullOrWhiteSpace(d.State) ||
                    string.IsNullOrWhiteSpace(d.ZipCode));
                if (invalidDelivery is not null)
                    return Results.BadRequest(new { Message = $"All address fields are required for every delivery. Missing field on: {invalidDelivery.CustomerName}" });

                try
                {
                    // Upsert an Address record for each unique address in this route.
                    // Using a dictionary keyed on normalized address to avoid duplicate DB lookups
                    // within the same batch.
                    var addressCache = new Dictionary<string, AddressEntity>();

                    foreach (var d in dto.Deliveries)
                    {
                        var street = d.Street.Trim();
                        var city = d.City.Trim();
                        var state = d.State.Trim();
                        var zip = d.ZipCode.Trim();
                        // Case-fold the cache key so "Main St" and "main st" in the
                        // same batch resolve to the same Address row. The DB
                        // lookup below is already case-insensitive via collation.
                        var key = $"{street}|{city}|{state}|{zip}".ToLowerInvariant();

                        if (addressCache.ContainsKey(key)) continue;

                        // Explicit case-insensitive collation. Azure SQL defaults to
                        // a CI collation so bare == would match in practice, but
                        // being explicit matches what /api/geocode uses and keeps
                        // us safe against any future instance that happens to be CS.
                        const string ci = "SQL_Latin1_General_CP1_CI_AS";
                        var address = await db.Addresses.FirstOrDefaultAsync(a =>
                            EF.Functions.Collate(a.Street, ci) == street
                            && EF.Functions.Collate(a.City, ci) == city
                            && EF.Functions.Collate(a.State, ci) == state
                            && EF.Functions.Collate(a.ZipCode, ci) == zip);

                        // Normalize the tank-location note: trim, and treat
                        // whitespace-only as "not provided" so we don't overwrite
                        // an existing stored value with an empty string.
                        var tankLocation = string.IsNullOrWhiteSpace(d.TankLocation)
                            ? null
                            : d.TankLocation.Trim();

                        if (address is null)
                        {
                            address = new AddressEntity
                            {
                                Id = Guid.NewGuid(),
                                Street = street,
                                City = city,
                                State = state,
                                ZipCode = zip,
                                Latitude = d.Latitude,
                                Longitude = d.Longitude,
                                AvgDeliveryTimeSeconds = 0,
                                TankLocation = tankLocation
                            };
                            db.Addresses.Add(address);
                        }
                        else
                        {
                            if (d.Latitude != 0 || d.Longitude != 0)
                            {
                                address.Latitude = d.Latitude;
                                address.Longitude = d.Longitude;
                            }

                            // Only update TankLocation when the caller actually
                            // supplied one — a null/blank value on a fresh route
                            // submit shouldn't wipe a previously-saved note.
                            if (tankLocation is not null)
                            {
                                address.TankLocation = tankLocation;
                            }
                        }

                        addressCache[key] = address;
                    }

                    // Flush addresses first so their IDs are stable before deliveries reference them.
                    await db.SaveChangesAsync();

                    var deliveries = dto.Deliveries.Select((d, i) =>
                    {
                        // Must match the normalization used when populating addressCache
                        // above — case-fold so a mixed-case duplicate resolves correctly.
                        var key = $"{d.Street.Trim()}|{d.City.Trim()}|{d.State.Trim()}|{d.ZipCode.Trim()}".ToLowerInvariant();
                        return new DeliveryEntity
                        {
                            Id = Guid.NewGuid(),
                            AddressId = addressCache[key].Id,
                            CustomerName = d.CustomerName,
                            AvgDeliveryTimeMinutes = d.AvgDeliveryTimeMinutes,
                            SortOrder = d.SortOrder == 0 ? i : d.SortOrder,
                            Status = 0,
                            CreatedAt = DateTime.UtcNow
                        };
                    }).ToList();

                    var estimatedRouteTime = await GPSHelperService.GetEstimatedRouteTime(
                        deliveries, addressCache.Values.ToList());

                    var route = new RouteEntity
                    {
                        Id = Guid.NewGuid(),
                        DriverId = driverId,
                        Date = dto.Date,
                        CreatedAt = DateTime.UtcNow,
                        EstimatedRouteTime = estimatedRouteTime,
                        Deliveries = deliveries
                    };

                    db.Routes.Add(route);
                    await db.SaveChangesAsync();

                    return Results.Ok(new { route.Id, DeliveryCount = route.Deliveries.Count });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create route for driver {DriverId}", dto.DriverId);
                    return Results.Problem(detail: ex.Message, title: "Failed to create route", statusCode: 500);
                }
            });

            return app;
        }
    }
}
