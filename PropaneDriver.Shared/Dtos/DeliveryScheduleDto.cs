namespace PropaneDriver.Shared.Dtos
{
    public class DeliveryScheduleDto
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public List<IDelivery> Deliveries { get; set; } = [];
        public int EmptyDaysInFirstWeek { get; set; }
    }
}
