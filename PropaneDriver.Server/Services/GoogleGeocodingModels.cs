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

        // Structured breakdown of the address. We parse this to fill the
        // Street/City/State/Zip fields on GeocodingResultDto so the Admin
        // form doesn't have to re-parse FormattedAddress.
        [JsonPropertyName("address_components")]
        public GoogleAddressComponent[] AddressComponents { get; set; } = Array.Empty<GoogleAddressComponent>();
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

    // One row out of Google's address_components array. Each component has
    // short/long names (state is "MN" short, "Minnesota" long) and one or
    // more type tags (e.g. "route", "locality", "administrative_area_level_1").
    public class GoogleAddressComponent
    {
        [JsonPropertyName("long_name")]
        public string LongName { get; set; } = string.Empty;

        [JsonPropertyName("short_name")]
        public string ShortName { get; set; } = string.Empty;

        [JsonPropertyName("types")]
        public string[] Types { get; set; } = Array.Empty<string>();
    }
}
