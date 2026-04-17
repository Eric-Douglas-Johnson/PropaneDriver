using System.Net;
using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class GeocodingService
    {
        private readonly HttpClient _http;

        public GeocodingService(HttpClient http)
        {
            _http = http;
        }

        public async Task<GeocodingResultDto?> GeocodeAsync(string street, string city, string state, string zip)
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(street)) query.Add($"street={Uri.EscapeDataString(street.Trim())}");
            if (!string.IsNullOrWhiteSpace(city)) query.Add($"city={Uri.EscapeDataString(city.Trim())}");
            if (!string.IsNullOrWhiteSpace(state)) query.Add($"state={Uri.EscapeDataString(state.Trim())}");
            if (!string.IsNullOrWhiteSpace(zip)) query.Add($"zip={Uri.EscapeDataString(zip.Trim())}");

            if (query.Count == 0) return null;

            try
            {
                var response = await _http.GetAsync($"api/geocode?{string.Join("&", query)}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "GeocodingService.GeocodeAsync",
                        $"api/geocode returned {(int)response.StatusCode}: {body}");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<GeocodingResultDto>();
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "GeocodingService.GeocodeAsync",
                    $"Exception geocoding address: {ex.Message}");
                return null;
            }
        }
    }
}
