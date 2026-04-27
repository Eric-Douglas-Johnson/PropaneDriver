using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class AlertDbRecord
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid DeliveryId { get; set; }

        [Required]
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public bool Seen { get; set; }

        // Navigation
        public DeliveryDbRecord? Delivery { get; set; }
    }
}
