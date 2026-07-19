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

        [Required]
        public double? Latitude { get; set; }
        [Required]
        public double? Longitude { get; set; }

        public int SortOrder { get; set; }
        public bool BackIn { get; set; }
        public bool LongRunning { get; set; }
    }
}
