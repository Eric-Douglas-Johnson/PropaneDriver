
using System.Net.Http.Json;

namespace PropaneDriver.Client.Services
{
    public static class ErrorLogService
    {
        private static readonly HttpClient _http = new HttpClient();

        public static async Task LogErrorAsync(string source, string message)
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
    }
}
