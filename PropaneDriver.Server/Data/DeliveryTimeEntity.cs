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
        [MaxLength(500)]
        public string Address { get; set; } = string.Empty;

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public double TimeIntervalSeconds { get; set; }

        public DateTime RecordedAt { get; set; }
    }
}
