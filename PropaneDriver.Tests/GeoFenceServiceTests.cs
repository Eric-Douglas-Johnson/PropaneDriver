using Microsoft.JSInterop;
using PropaneDriver.Client.Authentication;
using PropaneDriver.Client.Services;
using PropaneDriver.Client.ApiConsumers;
using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.Interfaces;

namespace PropaneDriver.Tests;

// Pins the rule that makes LongRunning stops safe from the geofence: a
// GeoFenceService holding no target does nothing with GPS updates. Route.razor
// routes LongRunning deliveries to SetTarget(null), so if a disarmed fence ever
// started doing work again, it would start its own timer on top of the driver's.
//
// Coordinates are real-ish rural-MN points around Hibbing, matching the
// convention in RouteProgressTrackerTests.
public class GeoFenceServiceTests
{
    private const double TankLatitude = 47.4273;
    private const double TankLongitude = -92.9378;

    // ~111 m north of the tank — outside the 150 ft default fence radius.
    private const double OutsideFenceLatitude = TankLatitude + 0.001;

    [Fact]
    public void PositionUpdateWithNoTargetDoesNotStartATimer()
    {
        var harness = new GeoFenceHarness();
        var fenceEventCount = 0;
        harness.GeoFence.OnFenceStatusChanged += _ => fenceEventCount++;

        // What Route.razor does for a LongRunning stop.
        harness.GeoFence.SetTarget(null);

        // A fix sitting exactly on the tank — inside the fence, had one been armed.
        harness.Geolocation.OnPositionUpdate(TankLatitude, TankLongitude, accuracy: 5);

        Assert.False(harness.DeliveryTimers.IsAnyTimerRunning);
        Assert.False(harness.GeoFence.HasArrived);
        Assert.Equal(0, fenceEventCount);
    }

    // Control for the test above: the same fix on an armed fence must start a
    // timer. Without this, the first test would still pass if the geofence
    // stopped working entirely.
    [Fact]
    public void PositionUpdateInsideAnArmedFenceStartsATimer()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();

        harness.GeoFence.SetTarget(delivery);
        harness.Geolocation.OnPositionUpdate(TankLatitude, TankLongitude, accuracy: 5);

        Assert.True(harness.DeliveryTimers.IsRunningFor(delivery.Id));
        Assert.True(harness.GeoFence.HasArrived);
    }

    [Fact]
    public void PositionUpdateOutsideAnArmedFenceDoesNotStartATimer()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();

        harness.GeoFence.SetTarget(delivery);
        harness.Geolocation.OnPositionUpdate(OutsideFenceLatitude, TankLongitude, accuracy: 5);

        Assert.False(harness.DeliveryTimers.IsAnyTimerRunning);
        Assert.False(harness.GeoFence.HasArrived);
    }

    // Disarming has to actually drop the previous target, not just reset the
    // inside-fence flag. A SetTarget that ignores null leaves the old delivery
    // armed, and the damage shows up on the NEXT entry into its fence — the
    // abandoned stop's timer restarts. This is the path a LongRunning
    // toggle takes when the page re-routes the target to null.
    [Fact]
    public void DisarmingDropsThePreviousTargetSoReenteringItsFenceDoesNothing()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();
        var fenceEventCount = 0;
        var completedCount = 0;
        harness.GeoFence.OnFenceStatusChanged += _ => fenceEventCount++;
        harness.DeliveryCompletion.OnDeliveryCompleted += (_, _) => completedCount++;

        harness.GeoFence.SetTarget(delivery);
        harness.Geolocation.OnPositionUpdate(OutsideFenceLatitude, TankLongitude, accuracy: 5);

        harness.GeoFence.SetTarget(null);
        fenceEventCount = 0;

        // Driving back onto the tank must not re-arm the abandoned stop.
        harness.Geolocation.OnPositionUpdate(TankLatitude, TankLongitude, accuracy: 5);

        Assert.False(harness.DeliveryTimers.IsAnyTimerRunning);
        Assert.False(harness.GeoFence.HasArrived);
        Assert.Equal(0, fenceEventCount);
        Assert.Equal(0, completedCount);
        Assert.Equal(0, delivery.Status);
    }

    // Leaving the fence no longer ends the stop — only the driver's Finish
    // tap does. The arrival and its timer survive the drive back out.
    [Fact]
    public void LeavingTheFenceKeepsTheStopArrivedAndItsTimerRunning()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();
        var completedCount = 0;
        var lastReportedArrival = false;
        harness.DeliveryCompletion.OnDeliveryCompleted += (_, _) => completedCount++;
        harness.GeoFence.OnFenceStatusChanged += args => lastReportedArrival = args.HasArrived;

        harness.GeoFence.SetTarget(delivery);
        harness.Geolocation.OnPositionUpdate(TankLatitude, TankLongitude, accuracy: 5);
        harness.Geolocation.OnPositionUpdate(OutsideFenceLatitude, TankLongitude, accuracy: 5);

        Assert.Equal(0, completedCount);
        Assert.Equal(0, delivery.Status);
        Assert.True(harness.DeliveryTimers.IsRunningFor(delivery.Id));
        Assert.True(harness.GeoFence.HasArrived);
        Assert.True(lastReportedArrival);
        Assert.True(harness.GeoFence.IsMonitoring);
    }

    // Finishing ends the stop the way fence exit used to, minus the 5-minute
    // minimum: this stop is only milliseconds long and still completes.
    [Fact]
    public async Task FinishingAnArrivedStopCompletesItAndDisarmsTheFence()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();
        var completedCount = 0;
        harness.DeliveryCompletion.OnDeliveryCompleted += (_, _) => completedCount++;

        harness.GeoFence.SetTarget(delivery);
        harness.Geolocation.OnPositionUpdate(TankLatitude, TankLongitude, accuracy: 5);

        // Guarantees a non-zero elapsed time; zero-second stops are rejected.
        await Task.Delay(20);

        var wasFinished = await harness.GeoFence.FinishArrivedDeliveryAsync();

        Assert.True(wasFinished);
        Assert.Equal(2, delivery.Status);
        Assert.Equal(1, completedCount);
        Assert.False(harness.DeliveryTimers.IsAnyTimerRunning);
        Assert.False(harness.GeoFence.HasArrived);
        Assert.False(harness.GeoFence.IsMonitoring);

        // Still parked at the tank: the finished stop must not restart.
        harness.Geolocation.OnPositionUpdate(TankLatitude, TankLongitude, accuracy: 5);
        Assert.False(harness.DeliveryTimers.IsAnyTimerRunning);
    }

    [Fact]
    public async Task FinishingBeforeArrivalDoesNothing()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();

        harness.GeoFence.SetTarget(delivery);
        harness.Geolocation.OnPositionUpdate(OutsideFenceLatitude, TankLongitude, accuracy: 5);

        var wasFinished = await harness.GeoFence.FinishArrivedDeliveryAsync();

        Assert.False(wasFinished);
        Assert.Equal(0, delivery.Status);
        Assert.True(harness.GeoFence.IsMonitoring);
    }

    // A finish that can't be saved (here: no linked address record) leaves the
    // stop pending and the fence armed, so the next fix inside it restarts the timer.
    [Fact]
    public async Task AFailedFinishLeavesTheStopArmedForARetry()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();
        delivery.Address.Id = Guid.Empty;

        harness.GeoFence.SetTarget(delivery);
        harness.Geolocation.OnPositionUpdate(TankLatitude, TankLongitude, accuracy: 5);
        await Task.Delay(20);

        var wasFinished = await harness.GeoFence.FinishArrivedDeliveryAsync();

        Assert.False(wasFinished);
        Assert.Equal(0, delivery.Status);
        Assert.True(harness.GeoFence.IsMonitoring);
        Assert.False(harness.GeoFence.HasArrived);

        harness.Geolocation.OnPositionUpdate(TankLatitude, TankLongitude, accuracy: 5);
        Assert.True(harness.DeliveryTimers.IsRunningFor(delivery.Id));
        Assert.True(harness.GeoFence.HasArrived);
    }

    [Fact]
    public async Task CompleteAsyncRejectsStopsUnderFiveMinutes()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();

        var wasCompleted = await harness.DeliveryCompletion.CompleteDeliveryAsync(
            delivery, rawElapsedSeconds: 299);

        Assert.False(wasCompleted);
        Assert.Equal(0, delivery.Status);
    }

    // The manual driver-stopped timer path opts out of the minimum: a
    // confirmed, deliberate stop counts no matter how short it was.
    [Fact]
    public async Task CompleteAsyncAllowsShortStopsWhenTheMinimumIsNotEnforced()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();

        var wasCompleted = await harness.DeliveryCompletion.CompleteDeliveryAsync(
            delivery, rawElapsedSeconds: 90, enforceMinimumDuration: false);

        Assert.True(wasCompleted);
        Assert.Equal(2, delivery.Status);
    }

    [Fact]
    public async Task CompleteAsyncCompletesStopsAtTheFiveMinuteMinimum()
    {
        var harness = new GeoFenceHarness();
        var delivery = DeliveryAtTank();

        var wasCompleted = await harness.DeliveryCompletion.CompleteDeliveryAsync(
            delivery, rawElapsedSeconds: 300);

        Assert.True(wasCompleted);
        Assert.Equal(2, delivery.Status);
    }

    private static PropaneDeliveryDto DeliveryAtTank() => new()
    {
        Id = Guid.NewGuid().ToString(),
        CustomerName = "Test Customer",
        Address = new AddressDto
        {
            Id = Guid.NewGuid(),
            Street = "123 Test Rd",
            Latitude = TankLatitude,
            Longitude = TankLongitude
        }
    };

    // Builds a GeoFenceService over fakes. GeolocationService.OnPositionUpdate is
    // public and [JSInvokable], so tests can push fixes without any JS.
    private sealed class GeoFenceHarness
    {
        public GeolocationService Geolocation { get; }
        public DeliveryTimerService DeliveryTimers { get; }
        public DeliveryCompletionService DeliveryCompletion { get; }
        public GeoFenceService GeoFence { get; }

        public GeoFenceHarness()
        {
            var jsRuntime = new NoOpJsRuntime();
            var http = new HttpClient(new NoOpHttpMessageHandler())
            {
                BaseAddress = new Uri("http://localhost")
            };

            Geolocation = new GeolocationService(jsRuntime);
            DeliveryTimers = new DeliveryTimerService(new BrowserStorageService(jsRuntime));
            DeliveryCompletion = new DeliveryCompletionService(
                new DeliveryTimeApiConsumer(http),
                new DeliveryApiConsumer(http));

            GeoFence = new GeoFenceService(Geolocation, DeliveryTimers, DeliveryCompletion);
        }
    }

    // localStorage reads come back empty; writes are discarded.
    private sealed class NoOpJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult<TValue>(default!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromResult<TValue>(default!);
    }

    private sealed class NoOpHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
    }
}
