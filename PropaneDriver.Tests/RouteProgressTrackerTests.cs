using PropaneDriver.Client.Services;

namespace PropaneDriver.Tests;

// Verifies the step-advance and distance-remaining rules in RouteProgressTracker.
// Coordinates are real-ish rural-MN points around Hibbing, matching the convention
// in GPSHelperServiceTests, so a regression in the Haversine call shows up as a
// wrong distance rather than a plausible-looking zero.
public class RouteProgressTrackerTests
{
    // Roughly 0.001 degrees of latitude ~= 111 m, which makes it easy to build
    // step endpoints a known distance apart.
    private const double BaseLatitude = 47.4273;
    private const double BaseLongitude = -92.9378;

    private static NavRouteStep Step(
        double endLatitude,
        double endLongitude,
        double distanceMeters,
        string instruction = "Continue",
        string maneuver = "")
        => new()
        {
            Instruction = instruction,
            Maneuver = maneuver,
            DistanceMeters = distanceMeters,
            EndLatitude = endLatitude,
            EndLongitude = endLongitude
        };

    // A straight three-step route heading due north, each step ~111 m long.
    private static List<NavRouteStep> ThreeStepNorthboundRoute() => new()
    {
        Step(BaseLatitude + 0.001, BaseLongitude, 111, "Turn left onto County Rd 7", "turn-left"),
        Step(BaseLatitude + 0.002, BaseLongitude, 111, "Turn right onto Main St", "turn-right"),
        Step(BaseLatitude + 0.003, BaseLongitude, 111, "Arrive at destination")
    };

    [Fact]
    public void Update_WithNoRoute_ReturnsEmptySnapshot()
    {
        var tracker = new RouteProgressTracker();

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.False(tracker.HasRoute);
        Assert.Null(snapshot.CurrentStep);
        Assert.False(snapshot.HasArrived);
        Assert.Equal(0, snapshot.MetersRemainingToDestination);
    }

    [Fact]
    public void SetSteps_WithNull_ClearsRouteWithoutThrowing()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());

        tracker.SetSteps(null);

        Assert.False(tracker.HasRoute);
        Assert.Null(tracker.Update(BaseLatitude, BaseLongitude).CurrentStep);
    }

    [Fact]
    public void Update_AtRouteStart_ReportsFirstManeuverAndFullRemainingDistance()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.NotNull(snapshot.CurrentStep);
        Assert.Equal("Turn left onto County Rd 7", snapshot.CurrentStep!.Instruction);
        Assert.Equal(0, tracker.CurrentStepIndex);

        // ~111 m to the first turn, plus the two later steps at 111 m each.
        Assert.InRange(snapshot.MetersToNextManeuver, 100, 125);
        Assert.InRange(snapshot.MetersRemainingToDestination, 320, 350);
    }

    [Fact]
    public void Update_AsDriverAdvancesAlongStep_CountsDistanceDown()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());

        var atStart = tracker.Update(BaseLatitude, BaseLongitude);
        var partWayAlong = tracker.Update(BaseLatitude + 0.0005, BaseLongitude);

        Assert.True(partWayAlong.MetersToNextManeuver < atStart.MetersToNextManeuver);
        Assert.True(partWayAlong.MetersRemainingToDestination < atStart.MetersRemainingToDestination);

        // Still working on the same maneuver — nothing has been reached yet.
        Assert.Equal(0, tracker.CurrentStepIndex);
    }

    [Fact]
    public void Update_OnReachingStepEnd_AdvancesToNextManeuver()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());
        tracker.Update(BaseLatitude, BaseLongitude);

        // Sitting on the first turn point.
        var snapshot = tracker.Update(BaseLatitude + 0.001, BaseLongitude);

        Assert.Equal(1, tracker.CurrentStepIndex);
        Assert.Equal("Turn right onto Main St", snapshot.CurrentStep!.Instruction);
    }

    [Fact]
    public void Update_WhenLaterPositionIsNearerAnEarlierStep_DoesNotGoBackward()
    {
        // The out-and-back case: the driver runs up a dead-end road, turns
        // around, and passes back through a point they've already been. Nearest-
        // endpoint matching would rewind the banner to a maneuver they finished.
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(new List<NavRouteStep>
        {
            Step(BaseLatitude + 0.001, BaseLongitude, 111, "Turn left at the fork", "turn-left"),
            Step(BaseLatitude + 0.004, BaseLongitude, 333, "Continue to the end of the road"),
            Step(BaseLatitude + 0.001, BaseLongitude, 333, "Turn around and come back"),
            Step(BaseLatitude - 0.002, BaseLongitude, 333, "Arrive at destination")
        });

        tracker.Update(BaseLatitude, BaseLongitude);
        tracker.Update(BaseLatitude + 0.004, BaseLongitude);
        var advancedIndex = tracker.CurrentStepIndex;

        // Back through the first turn point on the way out.
        tracker.Update(BaseLatitude + 0.001, BaseLongitude);

        Assert.True(tracker.CurrentStepIndex >= advancedIndex,
            "Step index must never move backward when the driver revisits an earlier point.");
    }

    [Fact]
    public void Update_WithSparseFixes_SkipsPastMultipleCompletedSteps()
    {
        // Simulates a GPS gap (dead zone, backgrounded tab): the driver
        // reappears sitting on the third turn point having never reported a
        // position at the first two.
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());
        tracker.Update(BaseLatitude, BaseLongitude);

        tracker.Update(BaseLatitude + 0.002, BaseLongitude);

        Assert.Equal(2, tracker.CurrentStepIndex);
    }

    [Fact]
    public void Update_PastFinalStep_ReportsArrival()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());
        tracker.Update(BaseLatitude, BaseLongitude);

        var snapshot = tracker.Update(BaseLatitude + 0.003, BaseLongitude);

        Assert.True(snapshot.HasArrived);
        Assert.Null(snapshot.CurrentStep);
    }

    [Fact]
    public void Update_OnSingleStepRoute_HandlesArrivalWithoutThrowing()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(new List<NavRouteStep>
        {
            Step(BaseLatitude + 0.001, BaseLongitude, 111, "Arrive at destination")
        });

        var beforeArrival = tracker.Update(BaseLatitude, BaseLongitude);
        var afterArrival = tracker.Update(BaseLatitude + 0.001, BaseLongitude);

        Assert.NotNull(beforeArrival.CurrentStep);
        Assert.True(afterArrival.HasArrived);
    }

    [Fact]
    public void SetSteps_AfterProgress_RewindsToFirstManeuver()
    {
        // Re-routing mid-trip must not leave the index pointing into the old route.
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());
        tracker.Update(BaseLatitude + 0.002, BaseLongitude);
        Assert.NotEqual(0, tracker.CurrentStepIndex);

        tracker.SetSteps(ThreeStepNorthboundRoute());

        Assert.Equal(0, tracker.CurrentStepIndex);
    }
}
