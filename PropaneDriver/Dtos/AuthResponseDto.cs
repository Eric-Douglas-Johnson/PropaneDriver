namespace PropaneDriver.Dtos
{
    public class AuthResponseDto
    {
        public bool IsAuthenticated { get; set; }
        public int Role { get; set; }
        public Guid UserId { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }
}
