using System.Security.Claims;

namespace PropaneDriver.Server.Authorization
{
    // Helpers for the self-or-admin ownership model. Endpoints that drivers
    // can hit for their own data and admins can hit for anyone's go through
    // CanAccessDriverData — keeping the rule in one place so we can audit
    // every spot we relax from AdminOnly without re-implementing it.
    public static class ClaimsPrincipalExtensions
    {
        // The driver's row id, parsed from the JWT NameIdentifier claim.
        // Returns null if the claim is missing or unparseable — callers
        // should treat that as "no signed-in driver" and reject.
        public static Guid? GetDriverId(this ClaimsPrincipal user)
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }

        public static bool IsAdmin(this ClaimsPrincipal user) =>
            user.IsInRole("admin");

        // True when the caller is an admin (admins can act on any driver's
        // data) or the target driverId matches their own claim.
        public static bool CanAccessDriverData(this ClaimsPrincipal user, Guid targetDriverId)
        {
            if (user.IsAdmin()) return true;
            var callerId = user.GetDriverId();
            return callerId.HasValue && callerId.Value == targetDriverId;
        }
    }
}
