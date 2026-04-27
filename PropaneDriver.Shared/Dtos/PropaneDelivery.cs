namespace PropaneDriver.Shared.Dtos
{
    public class PropaneDelivery : IDelivery
    {
        public string Id { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public GeoAddressDto Location { get; set; } = new();
        public double AvgDeliveryTimeMinutes { get; set; }
        public int Status { get; set; }
        public double? RecordedTimeSeconds { get; set; }
        public List<AlertDto> Alerts { get; set; } = new();

        public override string ToString()
        {
            return CustomerName;
        }
    }
}
