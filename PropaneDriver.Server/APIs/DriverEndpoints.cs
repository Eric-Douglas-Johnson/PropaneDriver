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
        // Matches the rule api/ResetPassword applies.
        private const int MinimumPasswordLength = 6;

        public static IEndpointRouteBuilder MapDriverEndpoints(this IEndpointRouteBuilder app)
        {
            // List all accounts. Supervisors and admins only — consumed by the
            // Admin page driver picker and the Tools page user list.
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

            // Create an account with any role. Admin-only; the public api/Register
            // endpoint can only create drivers.
            app.MapPost("api/drivers", async (CreateDriverDto dto, PropaneDriverDbContext db) =>
            {
                var userName = dto.UserName?.Trim() ?? string.Empty;
                if (userName.Length == 0 || string.IsNullOrWhiteSpace(dto.Password))
                    return Results.BadRequest(new { Message = "User name and password are required." });

                if (dto.Password.Length < MinimumPasswordLength)
                    return PasswordTooShort();

                var role = NormalizeRole(dto.Role);
                if (!UserRoles.All.Contains(role))
                    return UnknownRole();

                if (await db.Drivers.AnyAsync(d => d.UserName == userName))
                    return UserNameTaken();

                var driver = new DriverDbRecord
                {
                    Id = Guid.NewGuid(),
                    UserName = userName,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    Role = role,
                    FirstName = dto.FirstName?.Trim() ?? string.Empty,
                    MiddleName = dto.MiddleName?.Trim() ?? string.Empty,
                    LastName = dto.LastName?.Trim() ?? string.Empty,
                    Email = dto.Email?.Trim() ?? string.Empty,
                    PhoneNumber = dto.PhoneNumber?.Trim() ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                db.Drivers.Add(driver);
                await db.SaveChangesAsync();

                return Results.Ok(ToDriverDto(driver));
            }).RequireAuthorization("AdminOnly");

            // Edit an account's profile, role, and optionally its password. Admin-only.
            // Role and password changes reach the user at their next sign-in.
            app.MapPut("api/drivers/{id:guid}", async (
                Guid id,
                DriverUpdateDto dto,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                var userName = dto.UserName?.Trim() ?? string.Empty;
                if (userName.Length == 0)
                    return Results.BadRequest(new { Message = "User name is required." });

                var changePassword = !string.IsNullOrEmpty(dto.NewPassword);
                if (changePassword && dto.NewPassword.Length < MinimumPasswordLength)
                    return PasswordTooShort();

                var role = NormalizeRole(dto.Role);
                if (!UserRoles.All.Contains(role))
                    return UnknownRole();

                var driver = await db.Drivers.FindAsync(id);
                if (driver is null)
                    return Results.NotFound();

                // Blocks an admin from demoting themselves out of the only screen that can undo it.
                if (user.GetDriverId() == id && !string.Equals(role, driver.Role, StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { Message = "You can't change your own role." });

                if (await db.Drivers.AnyAsync(d => d.Id != id && d.UserName == userName))
                    return UserNameTaken();

                driver.UserName = userName;
                driver.FirstName = dto.FirstName?.Trim() ?? string.Empty;
                driver.MiddleName = dto.MiddleName?.Trim() ?? string.Empty;
                driver.LastName = dto.LastName?.Trim() ?? string.Empty;
                driver.Email = dto.Email?.Trim() ?? string.Empty;
                driver.PhoneNumber = dto.PhoneNumber?.Trim() ?? string.Empty;
                driver.Role = role;
                if (changePassword)
                    driver.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

                await db.SaveChangesAsync();

                return Results.Ok(ToDriverDto(driver));
            }).RequireAuthorization("AdminOnly");

            // Delete an account with its routes (deliveries and alerts cascade), fuel
            // log, and reset tokens. Admin-only; an admin can't delete themselves.
            app.MapDelete("api/drivers/{id:guid}", async (
                Guid id,
                ClaimsPrincipal user,
                PropaneDriverDbContext db) =>
            {
                if (user.GetDriverId() == id)
                    return Results.BadRequest(new { Message = "You can't delete your own account." });

                var driver = await db.Drivers.FindAsync(id);
                if (driver is null)
                    return Results.NotFound();

                db.Routes.RemoveRange(await db.Routes.Where(r => r.DriverId == id).ToListAsync());
                db.FuelLogEntries.RemoveRange(await db.FuelLogEntries.Where(f => f.DriverId == id).ToListAsync());
                db.PasswordResetTokens.RemoveRange(await db.PasswordResetTokens.Where(t => t.DriverId == id).ToListAsync());
                // Saved before the driver row: the model has no FK to the driver for EF to order these deletes by.
                await db.SaveChangesAsync();

                db.Drivers.Remove(driver);
                await db.SaveChangesAsync();

                return Results.NoContent();
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

                return Results.Ok(ToDriverDto(driver));
            }).RequireAuthorization("AuthenticatedDriver");

            return app;
        }

        private static string NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() ?? string.Empty;

        private static IResult UnknownRole() =>
            Results.BadRequest(new { Message = $"Role must be one of: {string.Join(", ", UserRoles.All)}." });

        private static IResult PasswordTooShort() =>
            Results.BadRequest(new { Message = $"Password must be at least {MinimumPasswordLength} characters." });

        private static IResult UserNameTaken() =>
            Results.Conflict(new { Message = "A user with that user name already exists." });

        private static DriverDto ToDriverDto(DriverDbRecord driver) => new()
        {
            Id = driver.Id.ToString(),
            UserName = driver.UserName,
            Role = driver.Role,
            FirstName = driver.FirstName,
            MiddleName = driver.MiddleName,
            LastName = driver.LastName,
            Email = driver.Email,
            PhoneNumber = driver.PhoneNumber
        };
    }
}
