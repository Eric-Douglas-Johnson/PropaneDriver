using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Services
{
    // Fetches national weekly average fuel prices from the U.S. Energy
    // Information Administration open-data API (https://api.eia.gov/v2)
    // and caches the result in memory. The home page shows these to give
    // drivers a current read on the propane / heating oil / diesel market.
    //
    // EIA publishes the residential propane and heating oil surveys weekly
    // from October through mid-March only; outside the heating season the
    // latest data point is simply the last week of the previous season,
    // which the DTO surfaces via PriceDate. Diesel is published year-round.
    //
    // Requires a free API key (https://www.eia.gov/opendata/register.php)
    // configured as "Eia:ApiKey" or the EIA_API_KEY environment variable.
    // Without a key the service returns an empty snapshot and the client
    // hides the price strip.
    public class EiaFuelPriceService
    {
        // EIA updates these series weekly, so anything tighter than a few
        // hours is wasted calls. Empty results (key missing, EIA outage)
        // are retried sooner so a transient failure doesn't blank the
        // home page for six hours.
        private static readonly TimeSpan SuccessfulCacheLifetime = TimeSpan.FromHours(6);
        private static readonly TimeSpan EmptyCacheLifetime = TimeSpan.FromMinutes(15);

        // Series id, API route the series lives under, and the label shown
        // on the home page. All three are national ("NUS") dollars-per-gallon
        // weekly averages.
        private static readonly (string SeriesId, string ApiRoute, string FuelName)[] TrackedFuelSeries =
        {
            ("W_EPLLPA_PRS_NUS_DPG", "petroleum/pri/wfr", "Residential Propane"),
            ("W_EPD2F_PRS_NUS_DPG", "petroleum/pri/wfr", "Residential Heating Oil"),
            ("EMD_EPD2D_PTE_NUS_DPG", "petroleum/pri/gnd", "On-Highway Diesel"),
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EiaFuelPriceService> _logger;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private FuelPriceSnapshotDto? _cachedSnapshot;
        private DateTimeOffset _cachedAtUtc;

        public EiaFuelPriceService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<EiaFuelPriceService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<FuelPriceSnapshotDto> GetSnapshotAsync()
        {
            if (CachedSnapshotIsFresh())
                return _cachedSnapshot!;

            await _refreshLock.WaitAsync();
            try
            {
                // Another request may have refreshed while we waited.
                if (CachedSnapshotIsFresh())
                    return _cachedSnapshot!;

                _cachedSnapshot = await FetchSnapshotFromEiaAsync();
                _cachedAtUtc = DateTimeOffset.UtcNow;
                return _cachedSnapshot;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private bool CachedSnapshotIsFresh()
        {
            if (_cachedSnapshot is null)
                return false;

            var lifetime = _cachedSnapshot.Prices.Count > 0
                ? SuccessfulCacheLifetime
                : EmptyCacheLifetime;

            return DateTimeOffset.UtcNow - _cachedAtUtc < lifetime;
        }

        private async Task<FuelPriceSnapshotDto> FetchSnapshotFromEiaAsync()
        {
            var apiKey = _configuration["Eia:ApiKey"]
                ?? Environment.GetEnvironmentVariable("EIA_API_KEY");

            var snapshot = new FuelPriceSnapshotDto();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning(
                    "EIA API key is not configured (Eia:ApiKey / EIA_API_KEY); home-page fuel prices will be hidden.");
                return snapshot;
            }

            var httpClient = _httpClientFactory.CreateClient();

            foreach (var (seriesId, apiRoute, fuelName) in TrackedFuelSeries)
            {
                try
                {
                    // Latest two weekly points, newest first, so we can show
                    // the current price plus the week-over-week change.
                    var requestUrl =
                        $"https://api.eia.gov/v2/{apiRoute}/data/?api_key={apiKey}" +
                        $"&frequency=weekly&data[0]=value&facets[series][]={seriesId}" +
                        "&sort[0][column]=period&sort[0][direction]=desc&offset=0&length=2";

                    var apiResponse = await httpClient.GetFromJsonAsync<EiaApiResponse>(requestUrl);
                    var dataPoints = apiResponse?.Response?.Data;

                    if (dataPoints is null || dataPoints.Count == 0)
                    {
                        _logger.LogWarning("EIA returned no data points for series {SeriesId}.", seriesId);
                        continue;
                    }

                    var latestPoint = dataPoints[0];
                    if (latestPoint.Period is null
                        || !DateOnly.TryParse(latestPoint.Period, CultureInfo.InvariantCulture, out var priceDate)
                        || !TryReadPrice(latestPoint.Value, out var latestPrice))
                    {
                        _logger.LogWarning("EIA returned an unparseable latest point for series {SeriesId}.", seriesId);
                        continue;
                    }

                    decimal? changeFromPriorWeek = null;
                    if (dataPoints.Count > 1 && TryReadPrice(dataPoints[1].Value, out var priorPrice))
                    {
                        changeFromPriorWeek = latestPrice - priorPrice;
                    }

                    snapshot.Prices.Add(new FuelPriceDto
                    {
                        FuelName = fuelName,
                        PriceDate = priceDate,
                        PricePerGallon = latestPrice,
                        ChangeFromPriorWeek = changeFromPriorWeek
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch EIA series {SeriesId}.", seriesId);
                }
            }

            return snapshot;
        }

        // EIA v2 serializes "value" as a number on some routes and a string
        // on others, so it has to come through as a raw JsonElement.
        private static bool TryReadPrice(JsonElement valueElement, out decimal price)
        {
            switch (valueElement.ValueKind)
            {
                case JsonValueKind.Number:
                    return valueElement.TryGetDecimal(out price);
                case JsonValueKind.String:
                    return decimal.TryParse(
                        valueElement.GetString(),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out price);
                default:
                    price = 0;
                    return false;
            }
        }

        private sealed class EiaApiResponse
        {
            [JsonPropertyName("response")]
            public EiaResponseBody? Response { get; set; }
        }

        private sealed class EiaResponseBody
        {
            [JsonPropertyName("data")]
            public List<EiaDataPoint> Data { get; set; } = new();
        }

        private sealed class EiaDataPoint
        {
            [JsonPropertyName("period")]
            public string? Period { get; set; }

            [JsonPropertyName("value")]
            public JsonElement Value { get; set; }
        }
    }
}
