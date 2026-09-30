using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Authorization;
using PropaneDriver.Server.Data;
using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Constants;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    // Account management for the Tools page: login identity, profile, and role
    // membership. Admin-only. Role-specific data stays with each role's own code.
    public static class UserEndpoints
    {
        // Matches the rule api/ResetPassword applies.
        private const int MinimumPasswordLength = 6;

        public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/users").RequireAuthorization("AdminOnly");

            // Every account, ordered by last then first name.
            group.MapGet("", async (PropaneDriverDbContext db, UserRoleService userRoleService) =>
            {
                var users = await db.Users
                    .AsNoTracking()
                    .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                    .ToListAsync();
                var rolesByUserId = await userRoleService.GetRolesByUserIdAsync();

                return Results.Ok(users.Select(u => ToUserDto(u, rolesByUserId.GetValueOrDefault(u.Id) ?? [])));
            });

            group.MapPost("", async (CreateUserDto dto, PropaneDriverDbContext db, UserRoleService userRoleService) =>
            {
                var userName = dto.UserName?.Trim() ?? string.Empty;
                if (userName.Length == 0 || string.IsNullOrWhiteSpace(dto.Password))
                    return Results.BadRequest(new { Message = "User name and password are required." });

                if (dto.Password.Length < MinimumPasswordLength)
                    return PasswordTooShort();

                var roles = NormalizeRoles(dto.Roles);
                if (roles is null)
                    return InvalidRoles();

                if (await db.Users.AnyAsync(u => u.UserName == userName))
                    return UserNameTaken();

                var newUser = new UserDbRecord
                {
                    Id = Guid.NewGuid(),
                    UserName = userName,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    FirstName = dto.FirstName?.Trim() ?? string.Empty,
                    MiddleName = dto.MiddleName?.Trim() ?? string.Empty,
                    LastName = dto.LastName?.Trim() ?? string.Empty,
                    Email = dto.Email?.Trim() ?? string.Empty,
                    PhoneNumber = dto.PhoneNumber?.Trim() ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(newUser);
                userRoleService.AddRoles(newUser.Id, roles);
                await db.SaveChangesAsync();

                return Results.Ok(ToUserDto(newUser, roles));
            });

            // Role and password changes reach the user at their next sign-in.
            group.MapPut("{id:guid}", async (
                Guid id,
                UserUpdateDto dto,
                ClaimsPrincipal caller,
                PropaneDriverDbContext db,
                UserRoleService userRoleService) =>
            {
                var userName = dto.UserName?.Trim() ?? string.Empty;
                if (userName.Length == 0)
                    return Results.BadRequest(new { Message = "User name is required." });

                var changePassword = !string.IsNullOrEmpty(dto.NewPassword);
                if (changePassword && dto.NewPassword.Length < MinimumPasswordLength)
                    return PasswordTooShort();

                var roles = NormalizeRoles(dto.Roles);
                if (roles is null)
                    return InvalidRoles();

                var existingUser = await db.Users.FindAsync(id);
                if (existingUser is null)
                    return Results.NotFound();

                // Blocks an admin from demoting themselves out of the only screen that can undo it.
                if (caller.GetUserId() == id && !roles.SequenceEqual(await userRoleService.GetRolesAsync(id)))
                    return Results.BadRequest(new { Message = "You can't change your own roles." });

                if (await db.Users.AnyAsync(u => u.Id != id && u.UserName == userName))
                    return UserNameTaken();

                var roleChangeRefusal = await userRoleService.SetRolesAsync(id, roles);
                if (roleChangeRefusal is not null)
                    return Results.BadRequest(new { Message = roleChangeRefusal });

                existingUser.UserName = userName;
                existingUser.FirstName = dto.FirstName?.Trim() ?? string.Empty;
                existingUser.MiddleName = dto.MiddleName?.Trim() ?? string.Empty;
                existingUser.LastName = dto.LastName?.Trim() ?? string.Empty;
                existingUser.Email = dto.Email?.Trim() ?? string.Empty;
                existingUser.PhoneNumber = dto.PhoneNumber?.Trim() ?? string.Empty;
                if (changePassword)
                    existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

                await db.SaveChangesAsync();

                return Results.Ok(ToUserDto(existingUser, roles));
            });

            // Deletes the account and everything its roles own. An admin can't delete themselves.
            group.MapDelete("{id:guid}", async (
                Guid id,
                ClaimsPrincipal caller,
                PropaneDriverDbContext db,
                UserRoleService userRoleService,
                DriverHistoryService driverHistoryService) =>
            {
                if (caller.GetUserId() == id)
                    return Results.BadRequest(new { Message = "You can't delete your own account." });

                var existingUser = await db.Users.FindAsync(id);
                if (existingUser is null)
                    return Results.NotFound();

                await driverHistoryService.RemoveHistoryAsync(id);
                await userRoleService.RemoveAllRolesAsync(id);
                db.PasswordResetTokens.RemoveRange(await db.PasswordResetTokens.Where(t => t.UserId == id).ToListAsync());
                db.Users.Remove(existingUser);
                await db.SaveChangesAsync();

                return Results.NoContent();
            });

            return app;
        }

        internal static UserDto ToUserDto(UserDbRecord user, IEnumerable<string> roles) => new()
        {
            Id = user.Id.ToString(),
            Roles = roles.ToList(),
            UserName = user.UserName,
            FirstName = user.FirstName,
            MiddleName = user.MiddleName,
            LastName = user.LastName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber
        };

        // Trimmed, lowercased and in UserRoles.All order; null when empty or any name is unknown.
        private static List<string>? NormalizeRoles(IEnumerable<string>? requestedRoles)
        {
            var normalizedRoles = (requestedRoles ?? [])
                .Select(role => role?.Trim().ToLowerInvariant() ?? string.Empty)
                .ToHashSet();

            if (normalizedRoles.Count == 0 || normalizedRoles.Any(role => !UserRoles.All.Contains(role)))
                return null;

            return UserRoles.All.Where(normalizedRoles.Contains).ToList();
        }

        private static IResult InvalidRoles() =>
            Results.BadRequest(new { Message = $"Choose at least one role from: {string.Join(", ", UserRoles.All)}." });

        private static IResult PasswordTooShort() =>
            Results.BadRequest(new { Message = $"Password must be at least {MinimumPasswordLength} characters." });

        private static IResult UserNameTaken() =>
            Results.Conflict(new { Message = "A user with that user name already exists." });
    }
}
