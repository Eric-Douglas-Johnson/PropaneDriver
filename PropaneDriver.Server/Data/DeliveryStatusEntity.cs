namespace PropaneDriver.Server.Data
{
    public class DeliveryStatusEntity
    {
        public string DeliveryId { get; set; } = string.Empty;
        public int Status { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
