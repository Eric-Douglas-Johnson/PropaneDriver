using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    // Thin wrapper over the /api/addresses endpoint group. Right now it only
    // handles the "set pin to current truck position" flow from the
    // Deliveries page; other address mutations still go through the route-
    // create path.
    public class AddressApiService
    {
        private readonly HttpClient _http;

        public AddressApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<bool> UpdateTankLocationAsync(Guid addressId, string? tankLocation)
        {
            if (addressId == Guid.Empty) return false;

            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/addresses/{addressId}/tank-location",
                    new AddressTankLocationUpdateDto { TankLocation = tankLocation });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "AddressApiService.UpdateTankLocationAsync",
                        $"PUT api/addresses/{addressId}/tank-location returned {(int)response.StatusCode}: {body}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "AddressApiService.UpdateTankLocationAsync",
                    $"Exception updating tank location for {addressId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateBackInAsync(Guid addressId, bool backIn)
        {
            if (addressId == Guid.Empty) return false;

            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/addresses/{addressId}/back-in",
                    new AddressBackInUpdateDto { BackIn = backIn });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "AddressApiService.UpdateBackInAsync",
                        $"PUT api/addresses/{addressId}/back-in returned {(int)response.StatusCode}: {body}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "AddressApiService.UpdateBackInAsync",
                    $"Exception updating back-in for {addressId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateLongRunningAsync(Guid addressId, bool longRunning)
        {
            if (addressId == Guid.Empty) return false;

            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/addresses/{addressId}/long-running",
                    new AddressLongRunningUpdateDto { LongRunning = longRunning });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "AddressApiService.UpdateLongRunningAsync",
                        $"PUT api/addresses/{addressId}/long-running returned {(int)response.StatusCode}: {body}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "AddressApiService.UpdateLongRunningAsync",
                    $"Exception updating long-running for {addressId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateCoordinatesAsync(Guid addressId, double latitude, double longitude)
        {
            if (addressId == Guid.Empty) return false;

            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/addresses/{addressId}/coordinates",
                    new AddressCoordinatesUpdateDto { Latitude = latitude, Longitude = longitude });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "AddressApiService.UpdateCoordinatesAsync",
                        $"PUT api/addresses/{addressId}/coordinates returned {(int)response.StatusCode}: {body}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "AddressApiService.UpdateCoordinatesAsync",
                    $"Exception updating coordinates for {addressId}: {ex.Message}");
                return false;
            }
        }
    }
}
