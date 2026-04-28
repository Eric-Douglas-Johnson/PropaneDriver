using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.JsonConverters;
using System.Text.Json.Serialization;

namespace PropaneDriver.Shared.Interfaces
{
    // Abstraction over a deliverable stop. Concrete implementations represent
    // different sources (manual entry today; CSV/CRM/tank-monitor pulls later).
    // Code that consumes a delivery should depend on IDelivery so a new source
    // can plug in without touching every call site.
    [JsonConverter(typeof(DeliveryJsonConverter))]
    public interface IDelivery
    {
        string Id { get; set; }
        DateOnly Date { get; set; }
        string CustomerName { get; set; }
        GeoAddressDto Location { get; set; }
        double AvgDeliveryTimeMinutes { get; set; }
        int Status { get; set; }
        double? RecordedTimeSeconds { get; set; }
        List<AlertDto> Alerts { get; set; }
    }
}
