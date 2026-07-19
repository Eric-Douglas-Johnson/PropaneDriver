using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Authorization;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;
using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Interfaces;

namespace PropaneDriver.Server.Endpoints
{
    public static class RouteEndpoints
    {
        public static IEndpointRouteBuilder MapRouteEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/routes");

            // Authenticated drivers may fetch routes for themselves; admin
            // pulls any driver's route. Self-or-admin ownership is enforced
            // server-side via the JWT NameIdentifier claim — a driver can't
            // peek at another driver's route by guessing the URL.
            group.MapGet("{driverId:guid}/{date}", async (
                Guid driverId,
                DateOnly date,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                if (!user.CanAccessDriverData(driverId))
                    return Results.Forbid();

                var route = await db.Routes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.DriverId == driverId && r.Date == date);

                if (route is null) return Results.NotFound();

                var routeDto = await BuildRouteDtoAsync(route, db);
                return Results.Ok(routeDto);
            }).RequireAuthorization("AuthenticatedDriver");

            // List all routes for a driver (summary info). Drivers may list
            // their own routes (Dispatch page); admins can list anyone's
            // (Admin page).
            group.MapGet("driver/{driverId:guid}", async (
                Guid driverId,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                if (!user.CanAccessDriverData(driverId))
                    return Results.Forbid();

                var routes = await db.Routes
                    .AsNoTracking()
                    .Where(r => r.DriverId == driverId)
                    .OrderByDescending(r => r.Date)
                    .Select(r => new RouteListItemDto
                    {
                        Id = r.Id.ToString(),
                        Date = r.Date,
                        DeliveryCount = db.Deliveries.Count(d => d.RouteId == r.Id),
                        CompletedCount = db.Deliveries.Count(d => d.RouteId == r.Id && d.Status == 2)
                    })
                    .ToListAsync();

                return Results.Ok(routes);
            }).RequireAuthorization("AuthenticatedDriver");

            // Delete a route and its deliveries. A driver can delete their
            // own routes (Dispatch); an admin can delete any (Admin).
            group.MapDelete("{id:guid}", async (
                Guid id,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                var route = await db.Routes.FindAsync(id);
                if (route is null) return Results.NotFound();

                if (!user.CanAccessDriverData(route.DriverId))
                    return Results.Forbid();

                db.Routes.Remove(route); // cascade deletes deliveries
                await db.SaveChangesAsync();
                return Results.Ok(new { Deleted = true, RouteId = id });
            }).RequireAuthorization("AuthenticatedDriver");

            // Admin: delete every route (and their deliveries) for a driver in
            // one shot, mirroring the bulk fuel-log delete on the Admin page.
            // Returns 404 when the driver has no routes so the client can report
            // there was nothing to delete rather than a phantom success.
            group.MapDelete("driver/{driverId:guid}", async (
                Guid driverId,
                PropaneDriverDbContext db) =>
            {
                var routes = await db.Routes
                    .Where(r => r.DriverId == driverId)
                    .ToListAsync();

                if (routes.Count == 0) return Results.NotFound();

                db.Routes.RemoveRange(routes); // cascade deletes deliveries
                await db.SaveChangesAsync();
                return Results.Ok(new { Deleted = routes.Count });
            }).RequireAuthorization("AdminOnly");

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
                    .FirstOrDefaultAsync(r => r.DriverId == driverId && r.Date == today);

                if (route is null) return Results.NotFound();

                var routeDto = await BuildRouteDtoAsync(route, db);

                // Hydrate the actual recorded delivery time on each completed
                // stop so the minimized "Complete" row can show the duration
                // after a page reload, when the client-side _completedTimesSeconds
                // dictionary is empty. The DeliveryTimes table is keyed by the
                // delivery's string Id (Guid.ToString()), so we group by it and
                // pick the most recent record per delivery.
                var deliveryIds = routeDto.Deliveries.Select(d => d.Id).ToList();
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

                    foreach (var d in routeDto.Deliveries)
                    {
                        if (latestByDelivery.TryGetValue(d.Id, out var seconds))
                            d.RecordedTimeSeconds = seconds;
                    }
                }

                return Results.Ok(routeDto);
            }).RequireAuthorization("AuthenticatedDriver");

            // Create a route with deliveries. Drivers create their own
            // routes via the Dispatch page; admins create on behalf of any
            // driver via the Admin page. The DriverId in the body is what's
            // used, but we reject it if a non-admin tries to create a route
            // for someone else.
            group.MapPost("", async (
                CreateRouteDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                if (!Guid.TryParse(dto.DriverId, out var driverId))
                    return Results.BadRequest(new { Message = "DriverId must be a valid GUID." });

                if (!user.CanAccessDriverData(driverId))
                    return Results.Forbid();


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
                    var addressCache = new Dictionary<string, AddressDbRecord>();

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

                        if (address is null)
                        {
                            // TankLocation stays null at create time — it's an
                            // address-only field set later via the address endpoint.
                            address = new AddressDbRecord
                            {
                                Id = Guid.NewGuid(),
                                Street = street,
                                City = city,
                                State = state,
                                ZipCode = zip,
                                Latitude = d.Latitude,
                                Longitude = d.Longitude,
                                AvgDeliveryTimeMinutes = 0,
                                BackIn = d.BackIn
                            };
                            db.Addresses.Add(address);
                        }
                        else
                        {
                            // Don't clobber a manually-set pin (UpdateCoordinatesAsync).
                            // Only seed coordinates from the create-form geocode when
                            // the address has none stored yet.
                            var hasCoords = (address.Latitude ?? 0) != 0 || (address.Longitude ?? 0) != 0;
                            if (!hasCoords && (d.Latitude != 0 || d.Longitude != 0))
                            {
                                address.Latitude = d.Latitude;
                                address.Longitude = d.Longitude;
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
                        return new DeliveryDbRecord
                        {
                            Id = Guid.NewGuid(),
                            AddressId = addressCache[key].Id,
                            CustomerName = d.CustomerName,
                            SortOrder = d.SortOrder == 0 ? i : d.SortOrder,
                            Status = 0,
                            LongRunning = d.LongRunning,
                            CreatedAt = DateTime.UtcNow
                        };
                    }).ToList();

                    var estimatedRouteTime = await GPSHelperService.GetEstimatedRouteTime(
                        deliveries, addressCache.Values.ToList());

                    var route = new RouteDbRecord
                    {
                        Id = Guid.NewGuid(),
                        DriverId = driverId,
                        Date = dto.Date,
                        CreatedAt = DateTime.UtcNow,
                        EstimatedRouteTime = estimatedRouteTime
                    };

                    // No navigation collection anymore: point each delivery at
                    // the route by FK and insert both explicitly.
                    foreach (var delivery in deliveries)
                        delivery.RouteId = route.Id;

                    db.Routes.Add(route);
                    db.Deliveries.AddRange(deliveries);
                    await db.SaveChangesAsync();

                    return Results.Ok(new { route.Id, DeliveryCount = deliveries.Count });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create route for driver {DriverId}", dto.DriverId);
                    return Results.Problem(detail: ex.Message, title: "Failed to create route", statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            // Append a single delivery to the end of an existing route.
            // Mirrors the address-upsert logic from POST api/routes so a brand-
            // new address gets created and an existing one is reused (with a
            // case-insensitive collation match). SortOrder is max+1 so the new
            // stop renders last in the table.
            group.MapPost("{routeId:guid}/deliveries", async (
                Guid routeId,
                CreateDeliveryDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                if (string.IsNullOrWhiteSpace(dto.CustomerName) ||
                    string.IsNullOrWhiteSpace(dto.Street) ||
                    string.IsNullOrWhiteSpace(dto.City) ||
                    string.IsNullOrWhiteSpace(dto.State) ||
                    string.IsNullOrWhiteSpace(dto.ZipCode) ||
                    dto.Latitude == 0.0 ||
                    dto.Longitude == 0.0)

                    return Results.BadRequest(new { Message = "Customer name, address fields, longitude and latitude are required." });

                try
                {
                    var route = await db.Routes
                        .FirstOrDefaultAsync(r => r.Id == routeId);

                    if (route is null) return Results.NotFound();
                    if (!user.CanAccessDriverData(route.DriverId)) return Results.Forbid();

                    var address = await GetExistingAddress(dto, db);

                    if (address is null) //address does not exist, so add
                    {
                        // LongRunning is per-delivery now; TankLocation is set
                        // separately via the address endpoint. Neither is written here.
                        address = new AddressDbRecord
                        {
                            Id = Guid.NewGuid(),
                            Street = dto.Street,
                            City = dto.City,
                            State = dto.State,
                            ZipCode = dto.ZipCode,
                            Latitude = dto.Latitude,
                            Longitude = dto.Longitude,
                            AvgDeliveryTimeMinutes = 0,
                            BackIn = dto.BackIn
                        };

                        db.Addresses.Add(address);
                    }

                    await db.SaveChangesAsync();

                    // Next sort order is max(existing)+1, computed in SQL so we
                    // don't need the route's (now-removed) delivery collection.
                    var maxSortOrder = await db.Deliveries
                        .Where(d => d.RouteId == route.Id)
                        .Select(d => (int?)d.SortOrder)
                        .MaxAsync();
                    var nextSort = maxSortOrder.HasValue ? maxSortOrder.Value + 1 : 0;

                    var delivery = new DeliveryDbRecord
                    {
                        Id = Guid.NewGuid(),
                        RouteId = route.Id,
                        AddressId = address.Id,
                        CustomerName = dto.CustomerName.Trim(),
                        SortOrder = nextSort,
                        Status = 0,
                        LongRunning = dto.LongRunning,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.Deliveries.Add(delivery);
                    await db.SaveChangesAsync();

                    // Recompute estimated route time now that the new stop is in.
                    var allDeliveries = await db.Deliveries
                        .Where(d => d.RouteId == route.Id)
                        .ToListAsync();
                    var addressIds = allDeliveries.Select(d => d.AddressId).Distinct().ToList();
                    var allAddresses = await db.Addresses
                        .Where(a => addressIds.Contains(a.Id))
                        .ToListAsync();
                    route.EstimatedRouteTime = await GPSHelperService.GetEstimatedRouteTime(
                        allDeliveries, allAddresses);
                    await db.SaveChangesAsync();

                    return Results.Ok(new
                    {
                        delivery.Id,
                        delivery.SortOrder,
                        route.EstimatedRouteTime
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to add delivery to route {RouteId}", routeId);
                    return Results.Problem(detail: ex.Message, title: "Failed to add delivery", statusCode: 500);
                }
            }).RequireAuthorization("AuthenticatedDriver");

            return app;
        }

        private static async Task<RouteDto> BuildRouteDtoAsync(RouteDbRecord route, PropaneDriverDbContext db)
        {
            var deliveriesWithAddress = await db.Deliveries
                .AsNoTracking()
                .Where(d => d.RouteId == route.Id)
                .OrderBy(d => d.SortOrder)
                .Join(db.Addresses,
                    d => d.AddressId,
                    a => a.Id,
                    (d, a) => new { Delivery = d, Address = a })
                .ToListAsync();

            var deliveryIds = deliveriesWithAddress.Select(x => x.Delivery.Id).ToList();

            var alertsByDelivery = (await db.Alerts
                    .AsNoTracking()
                    .Where(a => deliveryIds.Contains(a.DeliveryId))
                    .OrderBy(a => a.CreatedAt)
                    .ToListAsync())
                .GroupBy(a => a.DeliveryId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return new RouteDto
            {
                Id = route.Id.ToString(),
                DriverId = route.DriverId.ToString(),
                Date = route.Date,
                EstimatedRouteTime = route.EstimatedRouteTime,
                Deliveries = deliveriesWithAddress
                    .Select(x => (IDelivery)new PropaneDeliveryDto
                    {
                        Id = x.Delivery.Id.ToString(),
                        CustomerName = x.Delivery.CustomerName,
                        Date = route.Date,
                        LongRunning = x.Delivery.LongRunning,
                        Address = new AddressDto
                        {
                            Id = x.Address.Id,
                            Street = x.Address.Street,
                            City = x.Address.City,
                            State = x.Address.State,
                            ZipCode = x.Address.ZipCode,
                            Latitude = x.Address.Latitude ?? 0,
                            Longitude = x.Address.Longitude ?? 0,
                            AvgDeliveryTimeMinutes = x.Address.AvgDeliveryTimeMinutes,
                            TankLocation = x.Address.TankLocation,
                            BackIn = x.Address.BackIn
                        },
                        Status = x.Delivery.Status,
                        Alerts = (alertsByDelivery.TryGetValue(x.Delivery.Id, out var alerts)
                                ? alerts
                                : new List<AlertDbRecord>())
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
            };
        }

        private static async Task<AddressDbRecord?> GetExistingAddress(CreateDeliveryDto dto, PropaneDriverDbContext db)
        {
            var street = dto.Street.Trim();
            var city = dto.City.Trim();
            var state = dto.State.Trim();
            var zip = dto.ZipCode.Trim();

            const string ci = "SQL_Latin1_General_CP1_CI_AS";

            return await db.Addresses.FirstOrDefaultAsync(a =>
                EF.Functions.Collate(a.Street, ci) == street
                && EF.Functions.Collate(a.City, ci) == city
                && EF.Functions.Collate(a.State, ci) == state
                && EF.Functions.Collate(a.ZipCode, ci) == zip);
        }
    }
}
