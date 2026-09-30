using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.ApiConsumers
{
    public class DriverApiConsumer
    {
        private readonly HttpClient _http;

        public DriverApiConsumer(HttpClient http)
        {
            _http = http;
        }

        // Admin: change an account's role. True only when the server confirms the change.
        public async Task<bool> UpdateRoleAsync(string driverId, string role)
        {
            if (string.IsNullOrWhiteSpace(driverId)) return false;

            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/drivers/{driverId}/role", new DriverRoleUpdateDto { Role = role });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogApiConsumer.LogErrorAsync(
                        "DriverApiConsumer.UpdateRoleAsync",
                        $"PUT api/drivers/{driverId}/role returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(
                    "DriverApiConsumer.UpdateRoleAsync",
                    $"Exception updating role for {driverId}: {ex.Message}");
                return false;
            }
        }
    }
}
