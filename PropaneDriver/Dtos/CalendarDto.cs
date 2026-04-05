namespace PropaneDriver.Dtos
{
    public class CalendarDto
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public List<AppointmentDto> Appointments { get; set; } = [];
        public int EmptyDaysInFirstWeek { get; set; }
    }
}
