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
