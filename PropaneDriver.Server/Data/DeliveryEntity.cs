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
        public Guid AddressId { get; set; }

        [Required]
        [MaxLength(200)]
        public string CustomerName { get; set; } = string.Empty;

        public int Status { get; set; }

        public double AvgDeliveryTimeMinutes { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation
        public RouteEntity? Route { get; set; }
        public AddressEntity? Address { get; set; }
        public List<AlertEntity> Alerts { get; set; } = new();
    }
}
