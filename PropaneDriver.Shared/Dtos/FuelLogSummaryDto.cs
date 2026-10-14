namespace PropaneDriver.Shared.Dtos
{
    // One fuel log in a history list. Entries are loaded separately, by Id,
    // only when someone opens the log.
    public class FuelLogSummaryDto
    {
        public string Id { get; set; } = string.Empty;

        public string DriverId { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; }

        // Null while the log is still the driver's open, in-progress log.
        public DateTime? CompletedAt { get; set; }

        // Fill rows, not counting the Start baseline row.
        public int EntryCount { get; set; }

        // Sum of the positive per-row gallons, matching the Fuel Log page total.
        public decimal TotalGallons { get; set; }
    }
}
