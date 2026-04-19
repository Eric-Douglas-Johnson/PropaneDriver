namespace PropaneDriver.Shared.Dtos
{
    public class RouteListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public int DeliveryCount { get; set; }
        public int CompletedCount { get; set; }
    }
}
