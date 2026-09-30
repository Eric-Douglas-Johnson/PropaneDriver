using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    // Driver lookups. Account management (create, edit, roles, delete) lives in UserEndpoints.
    public static class DriverEndpoints
    {
        public static IEndpointRouteBuilder MapDriverEndpoints(this IEndpointRouteBuilder app)
        {
            // Every driver, ordered by last then first name. Supervisors and admins
            // only; consumed by the Admin page driver picker.
            app.MapGet("api/drivers", async (PropaneDriverDbContext db) =>
            {
                var driverUsers = await db.Drivers
                    .AsNoTracking()
                    .Join(db.Users, d => d.UserId, u => u.Id, (d, u) => u)
                    .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                    .ToListAsync();
                return Results.Ok(driverUsers.Select(ToDriverDto));
            }).RequireAuthorization("SupervisorOrAdmin");

            // Get driver by ID. Note: non-api prefix retained for backward compat
            // with existing clients that hit /driver/{id}. Any authenticated user
            // can fetch any driver; the listing endpoint above is the gated one.
            app.MapGet("driver/{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
            {
                var driverUser = await db.Drivers
                    .AsNoTracking()
                    .Where(d => d.UserId == id)
                    .Join(db.Users, d => d.UserId, u => u.Id, (d, u) => u)
                    .FirstOrDefaultAsync();

                if (driverUser is null)
                    return Results.NotFound();

                return Results.Ok(ToDriverDto(driverUser));
            }).RequireAuthorization("AuthenticatedDriver");

            return app;
        }

        // Mapped in .NET rather than SQL, which would render the Guid in uppercase.
        private static DriverDto ToDriverDto(UserDbRecord driverUser) => new()
        {
            Id = driverUser.Id.ToString(),
            UserName = driverUser.UserName,
            FirstName = driverUser.FirstName,
            MiddleName = driverUser.MiddleName,
            LastName = driverUser.LastName,
            Email = driverUser.Email,
            PhoneNumber = driverUser.PhoneNumber
        };
    }
}
