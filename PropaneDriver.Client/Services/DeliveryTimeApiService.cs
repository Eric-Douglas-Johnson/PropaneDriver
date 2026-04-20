using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class DeliveryTimeApiService
    {
        private readonly HttpClient _http;

        public DeliveryTimeApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<SaveDeliveryTimeResult> SaveDeliveryTimeAsync(DeliveryTimeDto dto)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/delivery-times", dto);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var msg = $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}";
                    Console.WriteLine($"Failed to save delivery time: {msg}");
                    return new SaveDeliveryTimeResult { Success = false, ErrorMessage = msg };
                }

                return new SaveDeliveryTimeResult { Success = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save delivery time: {ex.Message}");
                return new SaveDeliveryTimeResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<DeliveryAverageResult> GetAverageTimeAsync(GeoAddressDto location)
        {
            try
            {
                var street = Uri.EscapeDataString(location.Street);
                var city = Uri.EscapeDataString(location.City);
                var state = Uri.EscapeDataString(location.State);
                var zip = Uri.EscapeDataString(location.ZipCode);
                var result = await _http.GetFromJsonAsync<DeliveryAverageResult>(
                    $"api/delivery-times/average?street={street}&city={city}&state={state}&zip={zip}");
                return result ?? new DeliveryAverageResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get average time: {ex.Message}");
                return new DeliveryAverageResult();
            }
        }
    }

    public class SaveDeliveryTimeResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DeliveryAverageResult
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public double AverageSeconds { get; set; }
        public int Count { get; set; }
    }
}
