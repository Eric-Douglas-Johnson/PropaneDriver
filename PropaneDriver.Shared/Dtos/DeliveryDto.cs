namespace PropaneDriver.Shared.Dtos
{
    public class DeliveryDto
    {
        public string Id { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public string ScheduledTime { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double TankSize { get; set; }
        public double GallonsDelivered { get; set; }
        public int Status { get; set; }

        public override string ToString()
        {
            return CustomerName + " - " + ScheduledTime;
        }
    }
}
