
using System.Net.Http.Json;

namespace PropaneDriver.Client.Services
{
    public class ErrorLogService
    {
        private readonly HttpClient _http;

        public ErrorLogService(HttpClient http)
        {
            _http = http;
        }

        public async Task LogErrorAsync(string source, string message)
        {
            try
            {
                var payload = new
                {
                    Source = source,
                    Level = "Error",
                    Message = message,
                    Timestamp = DateTime.UtcNow
                };

                await _http.PostAsJsonAsync("api/client-logs", payload);
            }
            catch
            {
                // Don't let error logging itself cause failures
            }
        }
    }
}
