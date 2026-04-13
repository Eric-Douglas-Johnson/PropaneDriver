using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    public class ErrorLogEntity
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Source { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Level { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }
    }
}
