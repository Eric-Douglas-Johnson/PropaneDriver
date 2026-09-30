using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.ApiConsumers
{
    public class DeliveryTimeStatsApiConsumer
    {
        private readonly HttpClient _http;

        public DeliveryTimeStatsApiConsumer(HttpClient http)
        {
            _http = http;
        }

        // Dates are whole days; toDate is inclusive. Throws on failure so the caller can surface the error.
        public async Task<DeliveryTimeStatsApiDto?> GetDeliveryTimeStatsAsync(DateTime? fromDate, DateTime? toDate)
        {
            var queryParameters = new List<string>();

            if (fromDate.HasValue)
                queryParameters.Add($"from={Uri.EscapeDataString(DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc).ToString("O"))}");

            if (toDate.HasValue)
                queryParameters.Add($"to={Uri.EscapeDataString(DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc).ToString("O"))}");

            var queryString = queryParameters.Count == 0 ? string.Empty : "?" + string.Join("&", queryParameters);

            return await _http.GetFromJsonAsync<DeliveryTimeStatsApiDto>($"api/delivery-time-stats{queryString}");
        }
    }
}
