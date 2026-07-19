using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class AddressDbRecord
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [MinLength(1)]
        [MaxLength(200)]
        public string Street { get; set; } = string.Empty;

        [Required]
        [MinLength(1)]
        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [Required]
        [MinLength(1)]
        [MaxLength(50)]
        public string State { get; set; } = string.Empty;

        [Required]
        [MinLength(1)]
        [MaxLength(20)]
        public string ZipCode { get; set; } = string.Empty;

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        // Rolling average of recorded delivery durations for this address,
        // in minutes. Computed from the DeliveryTimes table.
        public double AvgDeliveryTimeMinutes { get; set; }

        [MaxLength(500)]
        public string? TankLocation { get; set; }

        // Driveway requires the driver to back the truck in (vs. pull
        // forward and turn around). Defaults false
        public bool BackIn { get; set; }
    }
}
