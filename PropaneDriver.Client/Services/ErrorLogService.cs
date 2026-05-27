
using System.Net.Http.Json;

namespace PropaneDriver.Client.Services
{
    public static class ErrorLogService
    {
        private static readonly HttpClient _http = new HttpClient();

        // Must be called once at startup (see Program.cs). Without a BaseAddress
        // the relative "api/client-logs" URL throws on every PostAsJsonAsync, so
        // client-side errors were silently lost on all platforms — which is why
        // the iPhone 401 storm left nothing in the server logs to diagnose.
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

            // LogErrorAsync is always invoked from inside a caller's catch block,
            // so it must never throw — a failure here (offline, base address not
            // set) would mask the original error we're trying to record.
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
