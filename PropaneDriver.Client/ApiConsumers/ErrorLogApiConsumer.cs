using System.Net.Http.Json;

namespace PropaneDriver.Client.ApiConsumers
{
    public static class ErrorLogApiConsumer
    {
        private static readonly HttpClient _http = new HttpClient();

        public static void Initialize(string baseAddress)
        {
            _http.BaseAddress = new Uri(baseAddress);
        }

        public static async Task LogErrorAsync(string source, string message)
        {
            var payload = new
            {
                Source = source,
                Level = "Error",
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                await _http.PostAsJsonAsync("api/client-logs", payload);
            }
            catch
            {
                // Best-effort logging; nothing more we can do from the client.
            }
        }
    }
}
