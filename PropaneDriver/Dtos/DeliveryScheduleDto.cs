namespace PropaneDriver.Dtos
{
    public class DeliveryScheduleDto
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public List<DeliveryDto> Deliveries { get; set; } = [];
        public int EmptyDaysInFirstWeek { get; set; }
    }
}
