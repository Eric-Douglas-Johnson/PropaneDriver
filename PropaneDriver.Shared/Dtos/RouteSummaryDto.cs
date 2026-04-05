namespace PropaneDriver.Shared.Dtos
{
    public class RouteSummaryDto
    {
        public DateOnly Date { get; set; }
        public int TotalStops { get; set; }
        public int CompletedStops { get; set; }
        public double TotalGallonsDelivered { get; set; }
        public double TotalGallonsRemaining { get; set; }
        public string EstimatedCompletionTime { get; set; } = string.Empty;
        public List<string> Alerts { get; set; } = [];
        public DriverDto? Driver { get; set; }
    }
}
