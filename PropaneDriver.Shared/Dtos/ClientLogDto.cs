namespace PropaneDriver.Shared.Dtos
{
    public class ClientLogDto
    {
        public string? Source { get; set; }
        public string? Level { get; set; }
        public string? Message { get; set; }
        public DateTime? Timestamp { get; set; }
    }
}
