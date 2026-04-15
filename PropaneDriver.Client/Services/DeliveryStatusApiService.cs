using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class DeliveryStatusApiService
    {
        private readonly HttpClient _http;
        private readonly ErrorLogService _errorLog;

        public DeliveryStatusApiService(HttpClient http, ErrorLogService errorLog)
        {
            _http = http;
            _errorLog = errorLog;
        }

        public async Task<bool> UpdateStatusAsync(string deliveryId, int status)
        {
            if (string.IsNullOrWhiteSpace(deliveryId))
                return false;

            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/deliveries/{Uri.EscapeDataString(deliveryId)}/status",
                    new DeliveryStatusUpdateDto { Status = status });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await _errorLog.LogErrorAsync(
                        "DeliveryStatusApiService.UpdateStatusAsync",
                        $"PUT api/deliveries/{deliveryId}/status returned {(int)response.StatusCode}: {body}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await _errorLog.LogErrorAsync(
                    "DeliveryStatusApiService.UpdateStatusAsync",
                    $"Exception updating status for {deliveryId}: {ex.Message}");
                return false;
            }
        }
    }
}
