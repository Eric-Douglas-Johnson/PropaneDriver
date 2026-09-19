using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class DriverEndpoints
    {
        public static IEndpointRouteBuilder MapDriverEndpoints(this IEndpointRouteBuilder app)
        {
            // List all drivers (for admin route-builder). Admin-only — the
            // driver picker on the Admin page is the sole consumer.
            app.MapGet("api/drivers", async (PropaneDriverDbContext db) =>
            {
                var drivers = await db.Drivers
                    .AsNoTracking()
                    .OrderBy(d => d.LastName).ThenBy(d => d.FirstName)
                    .Select(d => new DriverDto
                    {
                        Id = d.Id.ToString(),
                        UserName = d.UserName,
                        FirstName = d.FirstName,
                        MiddleName = d.MiddleName,
                        LastName = d.LastName,
                        Email = d.Email,
                        PhoneNumber = d.PhoneNumber,
                        Role = d.Role
                    })
                    .ToListAsync();
                return Results.Ok(drivers);
            }).RequireAuthorization("AdminOnly");

            // Get driver by ID. Note: non-api prefix retained for backward compat
            // with existing clients that hit /driver/{id}. Authenticated users can
            // fetch any driver record (used by the Route page to load the
            // currently-signed-in driver's name); the listing endpoint above is
            // the one that's admin-gated.
            app.MapGet("driver/{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
            {
                var driver = await db.Drivers.FindAsync(id);

                if (driver is null)
                    return Results.NotFound();

                return Results.Ok(new DriverDto
                {
                    Id = driver.Id.ToString(),
                    UserName = driver.UserName,
                    Role = driver.Role,
                    FirstName = driver.FirstName,
                    MiddleName = driver.MiddleName,
                    LastName = driver.LastName,
                    Email = driver.Email,
                    PhoneNumber = driver.PhoneNumber
                });
            }).RequireAuthorization("AuthenticatedDriver");

            return app;
        }
    }
}
