namespace PropaneDriver.Shared.Dtos
{
    public class CreateDeliveryDto
    {
        public string CustomerName { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double AvgDeliveryTimeMinutes { get; set; }
        public int SortOrder { get; set; }
    }
}
