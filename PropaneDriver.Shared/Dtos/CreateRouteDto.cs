namespace PropaneDriver.Shared.Dtos
{
    public class CreateRouteDto
    {
        public string DriverId { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public List<CreateDeliveryDto> Deliveries { get; set; } = new();
    }
}
