namespace PropaneDriver.Shared.Dtos
{
    // Request body for PUT api/deliveries/{id}/long-running. Toggles the
    // LongRunning flag on a single delivery so the driver client switches
    // between geofence auto-timer mode and manual Start/Stop button mode
    // for that stop.
    public class DeliveryLongRunningUpdateDto
    {
        public bool LongRunning { get; set; }
    }
}
