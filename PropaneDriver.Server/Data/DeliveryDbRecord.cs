
using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class DeliveryDbRecord
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid RouteId { get; set; }

        [Required]
        public Guid AddressId { get; set; }

        [Required]
        [MaxLength(200)]
        public string CustomerName { get; set; } = string.Empty;

        public int Status { get; set; }

        public int SortOrder { get; set; }

        // This stop skips the GPS-geofence delivery-time logic. The driver
        // taps Start/Stop buttons in the UI and the elapsed time is saved
        // when they stop. Per-delivery (not shared across an address).
        public bool LongRunning { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
