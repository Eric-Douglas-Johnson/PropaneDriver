using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PropaneDriver.Server.Services;

namespace PropaneDriver.Tests;

// EiaFuelPriceService backs the home-page "Fuel Market Snapshot": it pulls
// three weekly price series from the EIA open-data API, computes the
// week-over-week change, and caches the result. These tests stub the HTTP
// layer so no real EIA calls are made.
public class EiaFuelPriceServiceTests
{
    private const string EnvVarName = "EIA_API_KEY";

    // EIA serializes "value" as a JSON number on some routes and a string on
    // others; the canned payloads below intentionally cover both shapes.
    private static string EiaJson(string latestPeriod, string latestValue, string priorPeriod, string priorValue)
        => $$$"""
           {"response":{"total":"2","data":[
             {"period":"{{{latestPeriod}}}","value":{{{latestValue}}},"units":"$/GAL"},
             {"period":"{{{priorPeriod}}}","value":{{{priorValue}}},"units":"$/GAL"}
           ]}}
           """;

    private static EiaFuelPriceService CreateService(
        StubHttpMessageHandler handler, string? configuredApiKey = "test-key")
    {
        var configValues = new Dictionary<string, string?>();
        if (configuredApiKey is not null)
            configValues["Eia:ApiKey"] = configuredApiKey;

        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        return new EiaFuelPriceService(
            new StubHttpClientFactory(handler),
            config,
            NullLogger<EiaFuelPriceService>.Instance);
    }

    [Fact]
    public async Task Snapshot_IsEmptyAndMakesNoHttpCalls_WhenApiKeyMissing()
    {
        var originalEnv = Environment.GetEnvironmentVariable(EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(EnvVarName, null);
            var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException(
                "No HTTP call should be made without an API key."));
            var service = CreateService(handler, configuredApiKey: null);

            var snapshot = await service.GetSnapshotAsync();

            Assert.Empty(snapshot.Prices);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, originalEnv);
        }
    }

    [Fact]
    public async Task Snapshot_UsesEnvironmentVariable_WhenConfigPlaceholderIsEmpty()
    {
        // appsettings.json ships "Eia:ApiKey": "" — production sets the key
        // via the EIA_API_KEY app setting, and the empty placeholder must
        // not shadow it. (Regression test: the placeholder originally won.)
        var originalEnv = Environment.GetEnvironmentVariable(EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(EnvVarName, "env-key");
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    EiaJson("2026-06-08", "3.5", "2026-06-01", "3.4"),
                    Encoding.UTF8, "application/json")
            });
            var service = CreateService(handler, configuredApiKey: "");

            var snapshot = await service.GetSnapshotAsync();

            Assert.Equal(3, snapshot.Prices.Count);
            Assert.Equal(3, handler.RequestCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, originalEnv);
        }
    }

    [Fact]
    public async Task Snapshot_ParsesAllThreeSeries_WithWeeklyChange()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            // Number-typed values for the residential series, string-typed
            // for diesel, to exercise both JSON shapes EIA emits.
            string json;
            if (url.Contains("W_EPLLPA_PRS_NUS_DPG"))
                json = EiaJson("2026-03-16", "2.456", "2026-03-09", "2.426");
            else if (url.Contains("W_EPD2F_PRS_NUS_DPG"))
                json = EiaJson("2026-03-16", "3.581", "2026-03-09", "3.631");
            else if (url.Contains("EMD_EPD2D_PTE_NUS_DPG"))
                json = EiaJson("2026-06-08", "\"3.721\"", "2026-06-01", "\"3.721\"");
            else
                throw new InvalidOperationException($"Unexpected request: {url}");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var service = CreateService(handler);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(3, snapshot.Prices.Count);

        var propane = snapshot.Prices.Single(p => p.FuelName == "Residential Propane");
        Assert.Equal(2.456m, propane.PricePerGallon);
        Assert.Equal(0.030m, propane.ChangeFromPriorWeek);
        Assert.Equal(new DateOnly(2026, 3, 16), propane.PriceDate);

        var heatingOil = snapshot.Prices.Single(p => p.FuelName == "Residential Heating Oil");
        Assert.Equal(-0.050m, heatingOil.ChangeFromPriorWeek);

        var diesel = snapshot.Prices.Single(p => p.FuelName == "On-Highway Diesel");
        Assert.Equal(3.721m, diesel.PricePerGallon);
        Assert.Equal(0m, diesel.ChangeFromPriorWeek);
        Assert.Equal(new DateOnly(2026, 6, 8), diesel.PriceDate);
    }

    [Fact]
    public async Task Snapshot_KeepsOtherSeries_WhenOneSeriesFails()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("W_EPLLPA_PRS_NUS_DPG"))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    EiaJson("2026-06-08", "3.5", "2026-06-01", "3.4"),
                    Encoding.UTF8, "application/json")
            };
        });
        var service = CreateService(handler);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(2, snapshot.Prices.Count);
        Assert.DoesNotContain(snapshot.Prices, p => p.FuelName == "Residential Propane");
    }

    [Fact]
    public async Task Snapshot_IsCached_AcrossCalls()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                EiaJson("2026-06-08", "3.5", "2026-06-01", "3.4"),
                Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        var firstSnapshot = await service.GetSnapshotAsync();
        var secondSnapshot = await service.GetSnapshotAsync();

        Assert.Same(firstSnapshot, secondSnapshot);
        // One request per tracked series, and none for the second call.
        Assert.Equal(3, handler.RequestCount);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public int RequestCount { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly StubHttpMessageHandler _handler;

        public StubHttpClientFactory(StubHttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
