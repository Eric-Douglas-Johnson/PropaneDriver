
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

        // when true, skips the GPS-geofence delivery-time logic--uses manual timer
        public bool LongRunning { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
