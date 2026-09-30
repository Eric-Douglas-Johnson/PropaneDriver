using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Authorization;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Constants;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class DriverEndpoints
    {
        public static IEndpointRouteBuilder MapDriverEndpoints(this IEndpointRouteBuilder app)
        {
            // List all drivers (for the Admin page route-builder). Supervisors
            // and admins only — the driver picker on that page is the sole consumer.
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
            }).RequireAuthorization("SupervisorOrAdmin");

            // Change an account's role. Admin-only. The new role reaches the
            // user's JWT at their next sign-in.
            app.MapPut("api/drivers/{id:guid}/role", async (
                Guid id,
                DriverRoleUpdateDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                var newRole = dto.Role?.Trim().ToLowerInvariant() ?? string.Empty;
                if (!UserRoles.All.Contains(newRole))
                    return Results.BadRequest(new { Message = $"Role must be one of: {string.Join(", ", UserRoles.All)}." });

                // Blocks an admin from demoting themselves out of the only screen that can undo it.
                if (user.GetDriverId() == id)
                    return Results.BadRequest(new { Message = "You can't change your own role." });

                var driver = await db.Drivers.FindAsync(id);
                if (driver is null)
                    return Results.NotFound();

                driver.Role = newRole;
                await db.SaveChangesAsync();

                return Results.Ok(new { driver.Id, driver.Role });
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
