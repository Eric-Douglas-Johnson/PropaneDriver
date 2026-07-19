using PropaneDriver.Shared.Interfaces;

namespace PropaneDriver.Shared.Dtos
{
    public class PropaneDeliveryDto : IDelivery
    {
        public string Id { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public AddressDto Address { get; set; } = new();
        public bool LongRunning { get; set; }
        public int Status { get; set; }
        public double? RecordedTimeSeconds { get; set; }
        public List<AlertDto> Alerts { get; set; } = new();

        public override string ToString()
        {
            return CustomerName;
        }
    }
}
