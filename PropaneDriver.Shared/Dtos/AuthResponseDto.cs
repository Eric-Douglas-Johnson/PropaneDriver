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

        // The signed-in account's profile and roles, so the client can build
        // its claims without a second round-trip. Null when authentication failed.
        public UserDto? User { get; set; }
    }
}
