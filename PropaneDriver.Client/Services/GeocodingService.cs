using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PropaneDriver.Client.Services
{
    public class GeocodingService
    {
        private readonly HttpClient _http;

        public GeocodingService(HttpClient http)
        {
            // Use a separate HttpClient for external Nominatim calls
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "PropaneDriver/1.0");
        }

        public async Task<GeocodingResult?> GeocodeAsync(string street, string city, string state, string zip)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(street)) parts.Add(street.Trim());
            if (!string.IsNullOrWhiteSpace(city)) parts.Add(city.Trim());
            if (!string.IsNullOrWhiteSpace(state)) parts.Add(state.Trim());
            if (!string.IsNullOrWhiteSpace(zip)) parts.Add(zip.Trim());

            if (parts.Count == 0) return null;

            var query = Uri.EscapeDataString(string.Join(", ", parts));

            try
            {
                var url = $"https://nominatim.openstreetmap.org/search?q={query}&format=jsonv2&limit=1&addressdetails=0";
                var results = await _http.GetFromJsonAsync<List<NominatimResult>>(url);

                if (results is null || results.Count == 0) return null;

                var best = results[0];
                if (double.TryParse(best.Lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(best.Lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                {
                    return new GeocodingResult(lat, lon, best.DisplayName ?? string.Empty);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private class NominatimResult
        {
            [JsonPropertyName("lat")]
            public string Lat { get; set; } = string.Empty;

            [JsonPropertyName("lon")]
            public string Lon { get; set; } = string.Empty;

            [JsonPropertyName("display_name")]
            public string? DisplayName { get; set; }
        }
    }

    public record GeocodingResult(double Latitude, double Longitude, string DisplayName);
}
