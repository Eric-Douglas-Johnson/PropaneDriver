using System.Text.Json.Serialization;

namespace PropaneDriver.Server.Services
{
    // Top-level response from https://maps.googleapis.com/maps/api/geocode/json
    public class GoogleGeocodeResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("results")]
        public GoogleGeocodeResult[] Results { get; set; } = Array.Empty<GoogleGeocodeResult>();

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }

    public class GoogleGeocodeResult
    {
        [JsonPropertyName("formatted_address")]
        public string FormattedAddress { get; set; } = string.Empty;

        [JsonPropertyName("geometry")]
        public GoogleGeometry Geometry { get; set; } = new();
    }

    public class GoogleGeometry
    {
        [JsonPropertyName("location")]
        public GoogleLatLng Location { get; set; } = new();
    }

    public class GoogleLatLng
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }
    }
}
