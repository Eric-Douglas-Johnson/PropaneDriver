using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.ApiConsumers
{
    // Admin-only read side of api/client-logs. Writing stays on the static
    // ErrorLogApiConsumer, which has to work before anyone signs in.
    public class ErrorLogViewerApiConsumer
    {
        private readonly HttpClient _http;

        public ErrorLogViewerApiConsumer(HttpClient http)
        {
            _http = http;
        }

        // Newest first. Throws on failure so the caller can surface the error.
        public async Task<List<ErrorLogEntryDto>> GetErrorLogsAsync(int count)
        {
            return await _http.GetFromJsonAsync<List<ErrorLogEntryDto>>($"api/client-logs?count={count}") ?? new();
        }
    }
}
