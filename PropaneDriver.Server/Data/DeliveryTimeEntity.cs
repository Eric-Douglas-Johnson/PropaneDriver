using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class DeliveryTimeEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string DeliveryId { get; set; } = string.Empty;

        [Required]
        public Guid AddressId { get; set; }

        public double TimeIntervalSeconds { get; set; }

        public DateTime RecordedAt { get; set; }

        // Navigation
        public AddressEntity? Address { get; set; }
    }
}
