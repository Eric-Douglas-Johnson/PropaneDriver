using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class DeliveryEntity
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid RouteId { get; set; }

        [Required]
        [MaxLength(200)]
        public string CustomerName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Street { get; set; } = string.Empty;

        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [MaxLength(50)]
        public string State { get; set; } = string.Empty;

        [MaxLength(20)]
        public string ZipCode { get; set; } = string.Empty;

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public int Status { get; set; }

        public double AvgDeliveryTimeMinutes { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation
        public RouteEntity? Route { get; set; }
        public List<AlertEntity> Alerts { get; set; } = new();
    }
}
