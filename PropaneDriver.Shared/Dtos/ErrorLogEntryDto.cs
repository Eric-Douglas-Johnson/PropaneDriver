namespace PropaneDriver.Shared.Dtos
{
    // A stored error log row, as listed on the admin Tools page. Timestamp is UTC.
    public class ErrorLogEntryDto
    {
        public Guid Id { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
