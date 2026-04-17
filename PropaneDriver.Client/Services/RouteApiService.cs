using System.Net;
using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class RouteApiService
    {
        private readonly HttpClient _http;

        public RouteApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<RouteDto?> GetTodayRouteAsync(string driverId)
        {
            if (string.IsNullOrWhiteSpace(driverId)) return null;

            try
            {
                var response = await _http.GetAsync($"api/routes/today/{driverId}");
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "RouteApiService.GetTodayRouteAsync",
                        $"GET api/routes/today/{driverId} returned {(int)response.StatusCode}: {body}");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<RouteDto>();
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "RouteApiService.GetTodayRouteAsync",
                    $"Exception loading route for {driverId}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> CreateRouteAsync(CreateRouteDto dto)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/routes", dto);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "RouteApiService.CreateRouteAsync",
                        $"POST api/routes returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "RouteApiService.CreateRouteAsync",
                    $"Exception creating route: {ex.Message}");
                return false;
            }
        }
    }
}
