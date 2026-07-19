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

                    await ErrorLogService.LogErrorAsync(
                        "DeliveryTimeApiService.SaveDeliveryTimeAsync",
                        $"DeliveryId={dto.DeliveryId} AddressId={dto.AddressId}: {msg}");

                    return new SaveDeliveryTimeResult { Success = false, ErrorMessage = msg };
                }

                return new SaveDeliveryTimeResult { Success = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save delivery time: {ex.Message}");

                await ErrorLogService.LogErrorAsync(
                    "DeliveryTimeApiService.SaveDeliveryTimeAsync",
                    $"Exception saving delivery time DeliveryId={dto.DeliveryId} AddressId={dto.AddressId}: {ex.Message}");

                return new SaveDeliveryTimeResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<DeliveryAverageResult> GetAverageTimeAsync(Guid addressId)
        {
            try
            {
                var result = await _http.GetFromJsonAsync<DeliveryAverageResult>(
                    $"api/delivery-times/average?addressId={addressId}");
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
        public Guid AddressId { get; set; }
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public double AvgDeliveryTimeMinutes { get; set; }
    }
}
