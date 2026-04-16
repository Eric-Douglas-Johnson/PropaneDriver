using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class RouteEntity
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid DriverId { get; set; }

        public DateOnly Date { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation
        public List<DeliveryEntity> Deliveries { get; set; } = new();
    }
}
