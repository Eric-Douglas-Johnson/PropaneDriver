using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class FuelLogApiService
    {
        private readonly HttpClient _http;

        public FuelLogApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<FuelLogEntryDto>> GetFuelLogAsync()
        {
            try
            {
                var response = await _http.GetAsync("api/fuel-log");
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "FuelLogApiService.GetFuelLogAsync",
                        $"GET api/fuel-log returned {(int)response.StatusCode}: {body}");
                    return new();
                }

                return await response.Content.ReadFromJsonAsync<List<FuelLogEntryDto>>() ?? new();
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "FuelLogApiService.GetFuelLogAsync",
                    $"Exception loading fuel log: {ex.Message}");
                return new();
            }
        }

        // Admin: load a specific driver's fuel log so it can be reviewed (and
        // deleted) from the Admin page. Returns an empty list on any failure;
        // the failure is logged server-side and via ErrorLogService here.
        public async Task<List<FuelLogEntryDto>> GetFuelLogForDriverAsync(string driverId)
        {
            try
            {
                var response = await _http.GetAsync($"api/fuel-log/{driverId}");
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "FuelLogApiService.GetFuelLogForDriverAsync",
                        $"GET api/fuel-log/{driverId} returned {(int)response.StatusCode}: {body}");
                    return new();
                }

                return await response.Content.ReadFromJsonAsync<List<FuelLogEntryDto>>() ?? new();
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "FuelLogApiService.GetFuelLogForDriverAsync",
                    $"Exception loading fuel log for driver {driverId}: {ex.Message}");
                return new();
            }
        }

        // Admin: delete a driver's entire fuel log. True only when the server
        // confirms the delete (2xx); a 404 means there was nothing to delete and
        // is reported as a failure so the caller doesn't claim a phantom success.
        public async Task<bool> DeleteFuelLogForDriverAsync(string driverId)
        {
            try
            {
                var response = await _http.DeleteAsync($"api/fuel-log/{driverId}");
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "FuelLogApiService.DeleteFuelLogForDriverAsync",
                        $"DELETE api/fuel-log/{driverId} returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "FuelLogApiService.DeleteFuelLogForDriverAsync",
                    $"Exception deleting fuel log for driver {driverId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SaveFuelLogAsync(SaveFuelLogDto dto)
        {
            try
            {
                var response = await _http.PutAsJsonAsync("api/fuel-log", dto);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "FuelLogApiService.SaveFuelLogAsync",
                        $"PUT api/fuel-log returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "FuelLogApiService.SaveFuelLogAsync",
                    $"Exception saving fuel log: {ex.Message}");
                return false;
            }
        }
    }
}
