
namespace PropaneDriver.Client.Classes
{
    // Represents a timer that has been started for a delivery
    public class RunningTimer
    {
        public string DeliveryId { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
    }
}
