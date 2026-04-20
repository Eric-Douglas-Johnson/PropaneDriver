using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Shared.Dtos
{
    public class CreateDeliveryDto
    {
        [Required]
        [MinLength(1)]
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
    }
}
