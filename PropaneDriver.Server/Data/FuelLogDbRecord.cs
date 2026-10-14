using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    // One fuel log as a whole; its rows are FuelLogEntries keyed by FuelLogId.
    // A driver has at most one open log (CompletedAt null) that they're filling
    // in. Start new log completes it, which is what makes it a past log.
    public class FuelLogDbRecord
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid DriverId { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }
    }
}
