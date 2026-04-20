using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Shared.Dtos
{
    public class DeliveryTimeDto
    {
        public int Id { get; set; }
        public string DeliveryId { get; set; } = string.Empty;

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
        public double TimeIntervalSeconds { get; set; }
        public DateTime RecordedAt { get; set; }
    }
}
