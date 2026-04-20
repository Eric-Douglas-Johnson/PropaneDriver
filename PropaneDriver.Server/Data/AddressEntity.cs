using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class AddressEntity
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

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public double AvgDeliveryTimeSeconds { get; set; }

        // Navigation
        public List<DeliveryEntity> Deliveries { get; set; } = new();
        public List<DeliveryTimeEntity> DeliveryTimes { get; set; } = new();
    }
}
