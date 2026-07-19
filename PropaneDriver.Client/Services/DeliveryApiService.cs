using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class DeliveryApiService
    {
        private readonly HttpClient _http;

        public DeliveryApiService(HttpClient http)
        {
            _http = http;
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
                    await ErrorLogService.LogErrorAsync(
                        "DeliveryApiService.UpdateStatusAsync",
                        $"PUT api/deliveries/{deliveryId}/status returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryApiService.UpdateStatusAsync",
                    $"Exception updating status for {deliveryId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateLongRunningAsync(string deliveryId, bool longRunning)
        {
            if (string.IsNullOrWhiteSpace(deliveryId)) return false;

            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/deliveries/{deliveryId}/long-running",
                    new DeliveryLongRunningUpdateDto { LongRunning = longRunning });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "DeliveryApiService.UpdateLongRunningAsync",
                        $"PUT api/deliveries/{deliveryId}/long-running returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryApiService.UpdateLongRunningAsync",
                    $"Exception updating long-running for {deliveryId}: {ex.Message}");
                return false;
            }
        }
    }
}
