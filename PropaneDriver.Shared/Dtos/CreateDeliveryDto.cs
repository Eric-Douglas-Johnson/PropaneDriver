using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Shared.Dtos
{
    public class CreateDeliveryDto
    {
        public string CustomerName { get; set; } = string.Empty;

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

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double AvgDeliveryTimeMinutes { get; set; }
        public int SortOrder { get; set; }

        // Optional: free-text note about where the tank is on the
        // property. If null/empty on an existing Address the server
        // leaves the stored value untouched.
        [MaxLength(500)]
        public string? TankLocation { get; set; }

        public bool BackIn { get; set; }

        public bool LongRunning { get; set; }
    }
}
