namespace PropaneDriver.Shared.Dtos
{
    public class DeliveryTimeDto
    {
        public int Id { get; set; }
        public string DeliveryId { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double TimeIntervalSeconds { get; set; }
        public DateTime RecordedAt { get; set; }
    }
}
