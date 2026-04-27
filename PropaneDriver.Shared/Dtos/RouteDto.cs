namespace PropaneDriver.Shared.Dtos
{
    public class RouteDto
    {
        public string Id { get; set; } = string.Empty;
        public string DriverId { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public double EstimatedRouteTime { get; set; }
        public List<IDelivery> Deliveries { get; set; } = new();
    }
}
