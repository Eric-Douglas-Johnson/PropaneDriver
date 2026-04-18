namespace PropaneDriver.Shared.Dtos
{
    public class AlertDto
    {
        public string Id { get; set; } = string.Empty;
        public string DeliveryId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool Seen { get; set; }
    }

    public class CreateAlertDto
    {
        public string Message { get; set; } = string.Empty;
    }
}
