using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class RouteDbRecord
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid DriverId { get; set; }

        public DateOnly Date { get; set; }

        // Expected total route duration in minutes. Populated when the route is
        // planned; stays 0 until set.
        public double EstimatedRouteTime { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
