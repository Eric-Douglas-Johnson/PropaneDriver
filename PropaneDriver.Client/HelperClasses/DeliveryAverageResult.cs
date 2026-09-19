
namespace PropaneDriver.Client.HelperClasses
{
    public class DeliveryAverageResult
    {
        public Guid AddressId { get; set; }
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public double AvgDeliveryTimeMinutes { get; set; }
    }
}
