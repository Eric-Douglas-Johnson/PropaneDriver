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

        // Backoff between save retries. One entry per retry, so the total number
        // of attempts is this length plus the initial attempt.
        private static readonly TimeSpan[] SaveRetryBackoffDelays =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3)
        };

        // The fuel-log PUT is a full replace keyed by the signed-in driver, so
        // replaying the same save is idempotent and safe to retry. On the Free
        // hosting tier the worker idles out, and the first request after that
        // fails with the browser's "TypeError: Load failed" (a dead keep-alive
        // connection / cold start); the very same save succeeds moments later.
        // Retrying a couple of times with a short backoff makes that self-heal so
        // the driver never sees a spurious "Save failed".
        public async Task<bool> SaveFuelLogAsync(SaveFuelLogDto dto)
        {
            var totalAttempts = SaveRetryBackoffDelays.Length + 1;
            for (var attemptNumber = 1; attemptNumber <= totalAttempts; attemptNumber++)
            {
                var isFinalAttempt = attemptNumber == totalAttempts;
                try
                {
                    var response = await _http.PutAsJsonAsync("api/fuel-log", dto);
                    if (response.IsSuccessStatusCode)
                        return true;

                    // A 5xx is transient (cold start / restart) and worth retrying;
                    // a 4xx won't improve on retry, so report it and stop.
                    var isTransientStatus = (int)response.StatusCode >= 500;
                    if (isTransientStatus && !isFinalAttempt)
                    {
                        await Task.Delay(SaveRetryBackoffDelays[attemptNumber - 1]);
                        continue;
                    }

                    var body = await response.Content.ReadAsStringAsync();
                    await ErrorLogService.LogErrorAsync(
                        "FuelLogApiService.SaveFuelLogAsync",
                        $"PUT api/fuel-log returned {(int)response.StatusCode} after {attemptNumber} attempt(s): {body}");
                    return false;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Network-level failure: the browser's "TypeError: Load failed"
                    // or a timeout. These are the transient cold-start failures, so
                    // retry until the attempts are exhausted.
                    if (!isFinalAttempt)
                    {
                        await Task.Delay(SaveRetryBackoffDelays[attemptNumber - 1]);
                        continue;
                    }

                    await ErrorLogService.LogErrorAsync(
                        "FuelLogApiService.SaveFuelLogAsync",
                        $"Exception saving fuel log after {attemptNumber} attempt(s): {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    // Not a known-transient failure — don't retry, just report it
                    // the way this method always has.
                    await ErrorLogService.LogErrorAsync(
                        "FuelLogApiService.SaveFuelLogAsync",
                        $"Exception saving fuel log: {ex.Message}");
                    return false;
                }
            }

            // Unreachable — every path inside the loop returns — but the compiler
            // needs a terminal value.
            return false;
        }
    }
}
