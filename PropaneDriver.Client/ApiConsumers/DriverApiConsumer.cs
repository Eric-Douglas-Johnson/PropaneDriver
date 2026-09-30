using System.Net.Http.Json;
using System.Text.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.ApiConsumers
{
    public class DriverApiConsumer
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;

        public DriverApiConsumer(HttpClient http)
        {
            _http = http;
        }

        // Supervisor/admin: every account, ordered by last then first name. Null when the request fails.
        public async Task<List<DriverDto>?> GetDriversAsync()
        {
            try
            {
                return await _http.GetFromJsonAsync<List<DriverDto>>("api/drivers") ?? new();
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(
                    "DriverApiConsumer.GetDriversAsync",
                    $"Exception loading drivers: {ex.Message}");
                return null;
            }
        }

        // Admin: create an account. On a rejected request, ErrorMessage carries the server's reason.
        public async Task<(DriverDto? CreatedDriver, string? ErrorMessage)> CreateDriverAsync(CreateDriverDto newDriver)
        {
            const string source = "DriverApiConsumer.CreateDriverAsync";
            try
            {
                var response = await _http.PostAsJsonAsync("api/drivers", newDriver);

                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadFromJsonAsync<DriverDto>(), null);

                return (null, await ReadAndLogErrorAsync(response, source, "POST api/drivers"));
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(source, $"Exception creating user {newDriver.UserName}: {ex.Message}");
                return (null, ex.Message);
            }
        }

        // Admin: edit an account. On a rejected request, ErrorMessage carries the server's reason.
        public async Task<(DriverDto? UpdatedDriver, string? ErrorMessage)> UpdateDriverAsync(string driverId, DriverUpdateDto changes)
        {
            const string source = "DriverApiConsumer.UpdateDriverAsync";
            try
            {
                var response = await _http.PutAsJsonAsync($"api/drivers/{driverId}", changes);

                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadFromJsonAsync<DriverDto>(), null);

                return (null, await ReadAndLogErrorAsync(response, source, $"PUT api/drivers/{driverId}"));
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(source, $"Exception updating user {driverId}: {ex.Message}");
                return (null, ex.Message);
            }
        }

        // Admin: delete an account and everything it owns.
        public async Task<(bool Deleted, string? ErrorMessage)> DeleteDriverAsync(string driverId)
        {
            const string source = "DriverApiConsumer.DeleteDriverAsync";
            try
            {
                var response = await _http.DeleteAsync($"api/drivers/{driverId}");

                if (response.IsSuccessStatusCode)
                    return (true, null);

                return (false, await ReadAndLogErrorAsync(response, source, $"DELETE api/drivers/{driverId}"));
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(source, $"Exception deleting user {driverId}: {ex.Message}");
                return (false, ex.Message);
            }
        }

        // Logs a failed response and returns the server's { message } text, or a generic fallback.
        private static async Task<string> ReadAndLogErrorAsync(HttpResponseMessage response, string source, string request)
        {
            var body = await response.Content.ReadAsStringAsync();
            await ErrorLogApiConsumer.LogErrorAsync(source, $"{request} returned {(int)response.StatusCode}: {body}");

            string? serverMessage = null;
            try { serverMessage = JsonSerializer.Deserialize<ApiErrorBody>(body, JsonOptions)?.Message; }
            catch (JsonException) { }

            return serverMessage ?? $"Request failed ({(int)response.StatusCode}).";
        }

        private sealed record ApiErrorBody(string? Message);
    }
}
