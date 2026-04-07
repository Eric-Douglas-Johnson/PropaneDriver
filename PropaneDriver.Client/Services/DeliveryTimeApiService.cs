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

        public async Task SaveDeliveryTimeAsync(DeliveryTimeDto dto)
        {
            try
            {
                await _http.PostAsJsonAsync("api/delivery-times", dto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save delivery time: {ex.Message}");
            }
        }

        public async Task<DeliveryAverageResult> GetAverageTimeAsync(string address)
        {
            try
            {
                var encoded = Uri.EscapeDataString(address);
                var result = await _http.GetFromJsonAsync<DeliveryAverageResult>($"api/delivery-times/average/{encoded}");
                return result ?? new DeliveryAverageResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get average time: {ex.Message}");
                return new DeliveryAverageResult();
            }
        }
    }

    public class DeliveryAverageResult
    {
        public string Address { get; set; } = string.Empty;
        public double AverageSeconds { get; set; }
        public int Count { get; set; }
    }
}
