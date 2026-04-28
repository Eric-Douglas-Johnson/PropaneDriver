namespace PropaneDriver.Shared.Dtos
{
    // Request body for PUT api/addresses/{id}/long-running. Toggles the
    // LongRunning flag so the driver client switches between geofence
    // auto-timer mode and manual Start/Stop button mode for this address.
    public class AddressLongRunningUpdateDto
    {
        public bool LongRunning { get; set; }
    }
}
