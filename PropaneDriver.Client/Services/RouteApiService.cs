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

        public async Task<List<RouteListItemDto>> GetRoutesForDriverAsync(string driverId)
        {
            if (string.IsNullOrWhiteSpace(driverId)) return new();

            try
            {
                var response = await _http.GetAsync($"api/routes/driver/{driverId}");
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "RouteApiService.GetRoutesForDriverAsync",
                        $"GET api/routes/driver/{driverId} returned {(int)response.StatusCode}: {body}");
                    return new();
                }

                return await response.Content.ReadFromJsonAsync<List<RouteListItemDto>>() ?? new();
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "RouteApiService.GetRoutesForDriverAsync",
                    $"Exception loading routes for {driverId}: {ex.Message}");
                return new();
            }
        }

        public async Task<bool> DeleteRouteAsync(string routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId)) return false;

            try
            {
                var response = await _http.DeleteAsync($"api/routes/{routeId}");
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "RouteApiService.DeleteRouteAsync",
                        $"DELETE api/routes/{routeId} returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "RouteApiService.DeleteRouteAsync",
                    $"Exception deleting route {routeId}: {ex.Message}");
                return false;
            }
        }

        // Admin: delete every route for a driver. True only when the server
        // confirms (2xx); a 404 means there were no routes to delete and is
        // reported as failure so the caller doesn't claim a phantom success.
        public async Task<bool> DeleteAllRoutesForDriverAsync(string driverId)
        {
            if (string.IsNullOrWhiteSpace(driverId)) return false;

            try
            {
                var response = await _http.DeleteAsync($"api/routes/driver/{driverId}");
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "RouteApiService.DeleteAllRoutesForDriverAsync",
                        $"DELETE api/routes/driver/{driverId} returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "RouteApiService.DeleteAllRoutesForDriverAsync",
                    $"Exception deleting all routes for {driverId}: {ex.Message}");
                return false;
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

        public async Task<bool> AddDeliveryToRouteAsync(string routeId, CreateDeliveryDto dto)
        {
            if (string.IsNullOrWhiteSpace(routeId)) return false;

            try
            {
                var response = await _http.PostAsJsonAsync($"api/routes/{routeId}/deliveries", dto);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "RouteApiService.AddDeliveryToRouteAsync",
                        $"POST api/routes/{routeId}/deliveries returned {(int)response.StatusCode}: {body}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "RouteApiService.AddDeliveryToRouteAsync",
                    $"Exception adding delivery to route {routeId}: {ex.Message}");
                return false;
            }
        }
    }
}
