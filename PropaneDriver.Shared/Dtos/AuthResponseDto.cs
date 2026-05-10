namespace PropaneDriver.Shared.Dtos
{
    public class AuthResponseDto
    {
        public bool IsAuthenticated { get; set; }
        public Guid UserId { get; set; }
        public string StatusMessage { get; set; } = string.Empty;

        // JWT bearer token issued by the server on a successful authenticate.
        // Empty when IsAuthenticated is false.
        public string Token { get; set; } = string.Empty;

        // Full driver profile + role string ("driver" or "admin"). Avoids the
        // historical second GET /driver/{id} round-trip on login. Null when
        // authentication failed.
        public DriverDto? Driver { get; set; }
    }
}
