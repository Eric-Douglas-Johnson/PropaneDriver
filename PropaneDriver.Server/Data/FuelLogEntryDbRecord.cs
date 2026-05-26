using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    // A single fuel-log row owned by a driver. The log is edited and saved as
    // a whole on the client, so rows carry a SortOrder to preserve the
    // sequence the order-dependent gallons-pumped calculation relies on.
    public class FuelLogEntryDbRecord
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid DriverId { get; set; }

        [Required]
        [MaxLength(100)]
        public string EquipmentNumber { get; set; } = string.Empty;

        public decimal MeterValue { get; set; }

        public decimal GallonsPumped { get; set; }

        public int SortOrder { get; set; }

        public DateTime RecordedAt { get; set; }
    }
}
