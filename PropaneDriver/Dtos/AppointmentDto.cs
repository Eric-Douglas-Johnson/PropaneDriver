namespace PropaneDriver.Dtos
{
    public class AppointmentDto
    {
        public string Id { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public ProviderDto? User { get; set; }
        public int Type { get; set; }

        public override string ToString()
        {
            if (User == null) return string.Empty;
            return User.UserName + " - " + StartTime;
        }
    }
}
