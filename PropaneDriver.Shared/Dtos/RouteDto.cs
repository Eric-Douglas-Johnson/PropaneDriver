namespace PropaneDriver.Shared.Dtos
{
    public class RouteDto
    {
        public string Id { get; set; } = string.Empty;
        public string DriverId { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public List<DeliveryDto> Deliveries { get; set; } = new();
    }

    public class DeliveryStatusUpdateDto
    {
        public int Status { get; set; }
    }

    public class RouteListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public int DeliveryCount { get; set; }
        public int CompletedCount { get; set; }
    }

    public class CreateRouteDto
    {
        public string DriverId { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public List<CreateDeliveryDto> Deliveries { get; set; } = new();
    }

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
