using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

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
                        Deliveries = r.Deliveries
                            .OrderBy(d => d.SortOrder)
                            .Select(d => new DeliveryDto
                            {
                                Id = d.Id.ToString(),
                                CustomerName = d.CustomerName,
                                Date = r.Date,
                                Location = new GeoAddressDto
                                {
                                    Street = d.Street,
                                    City = d.City,
                                    State = d.State,
                                    ZipCode = d.ZipCode,
                                    Latitude = d.Latitude,
                                    Longitude = d.Longitude
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

            // Get today's route (with deliveries + alerts) for a driver
            group.MapGet("today/{driverId:guid}", async (Guid driverId, PropaneDriverDbContext db) =>
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var route = await db.Routes
                    .AsNoTracking()
                    .Where(r => r.DriverId == driverId && r.Date == today)
                    .Select(r => new RouteDto
                    {
                        Id = r.Id.ToString(),
                        DriverId = r.DriverId.ToString(),
                        Date = r.Date,
                        Deliveries = r.Deliveries
                            .OrderBy(d => d.SortOrder)
                            .Select(d => new DeliveryDto
                            {
                                Id = d.Id.ToString(),
                                CustomerName = d.CustomerName,
                                Date = r.Date,
                                Location = new GeoAddressDto
                                {
                                    Street = d.Street,
                                    City = d.City,
                                    State = d.State,
                                    ZipCode = d.ZipCode,
                                    Latitude = d.Latitude,
                                    Longitude = d.Longitude
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

            // Create a route with deliveries
            group.MapPost("", async (CreateRouteDto dto, PropaneDriverDbContext db, ILogger<Program> logger) =>
            {
                if (!Guid.TryParse(dto.DriverId, out var driverId))
                    return Results.BadRequest(new { Message = "DriverId must be a valid GUID." });

                try
                {
                    var route = new RouteEntity
                    {
                        Id = Guid.NewGuid(),
                        DriverId = driverId,
                        Date = dto.Date,
                        CreatedAt = DateTime.UtcNow,
                        Deliveries = dto.Deliveries.Select((d, i) => new DeliveryEntity
                        {
                            Id = Guid.NewGuid(),
                            CustomerName = d.CustomerName,
                            Street = d.Street,
                            City = d.City,
                            State = d.State,
                            ZipCode = d.ZipCode,
                            Latitude = d.Latitude,
                            Longitude = d.Longitude,
                            AvgDeliveryTimeMinutes = d.AvgDeliveryTimeMinutes,
                            SortOrder = d.SortOrder == 0 ? i : d.SortOrder,
                            Status = 0,
                            CreatedAt = DateTime.UtcNow
                        }).ToList()
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
