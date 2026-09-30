
using System.ComponentModel.DataAnnotations;
using PropaneDriver.Shared.Enums;

namespace PropaneDriver.Server.Data
{
    public class RouteDbRecord
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid DriverId { get; set; }

        public DateOnly Date { get; set; }

        // Expected total route duration in minutes.
        public double EstimatedRouteTime { get; set; }

        public DateTime CreatedAt { get; set; }

        // Propane, Fuel Oil, etc
        public ProductType ProductType { get; set; }
    }
}
