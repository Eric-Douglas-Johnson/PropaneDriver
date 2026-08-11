using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.JsonConverters;
using System.Text.Json.Serialization;

namespace PropaneDriver.Shared.Interfaces
{
    // Abstraction over a deliverable stop.
    // Code that consumes a delivery should depend on IDelivery so a new source
    // can plug in without touching every call site.
    [JsonConverter(typeof(DeliveryJsonConverter))]
    public interface IDelivery
    {
        string Id { get; set; }
        DateOnly Date { get; set; }
        string CustomerName { get; set; }
        AddressDto Address { get; set; }
        // Per-delivery flag: this stop uses manual Start/Stop timing instead
        // of the GPS geofence.
        bool LongRunning { get; set; }
        int Status { get; set; }
        double? RecordedTimeSeconds { get; set; }
        List<AlertDto> Alerts { get; set; }
    }
}
