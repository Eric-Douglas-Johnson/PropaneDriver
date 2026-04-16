using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class DeliveryApiService
    {
        private readonly HttpClient _http;
        private readonly ErrorLogService _errorLog;

        public DeliveryApiService(HttpClient http, ErrorLogService errorLog)
        {
            _http = http;
            _errorLog = errorLog;
        }

        public async Task<bool> UpdateStatusAsync(string deliveryId, int status)
        {
            if (string.IsNullOrWhiteSpace(deliveryId)) return false;

            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/deliveries/{deliveryId}/status",
                    new DeliveryStatusUpdateDto { Status = status });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await _errorLog.LogErrorAsync(
                        "DeliveryApiService.UpdateStatusAsync",
                        $"PUT api/deliveries/{deliveryId}/status returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await _errorLog.LogErrorAsync(
                    "DeliveryApiService.UpdateStatusAsync",
                    $"Exception updating status for {deliveryId}: {ex.Message}");
                return false;
            }
        }
    }
}
