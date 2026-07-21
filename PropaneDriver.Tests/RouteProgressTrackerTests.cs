using PropaneDriver.Client.Services;

namespace PropaneDriver.Tests;

// Verifies the step-advance, maneuver-selection and distance-remaining rules in
// RouteProgressTracker. Coordinates are real-ish rural-MN points around Hibbing,
// matching the convention in GPSHelperServiceTests, so a regression in the
// Haversine call shows up as a wrong distance rather than a plausible zero.
//
// Google's step indexing is the thing to keep straight throughout: a step's
// Instruction describes the maneuver at its START, and its end point is where
// the NEXT step's maneuver happens. Driving step i means approaching step i+1's
// turn.
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
    // Departure, then a left, then arrival.
    private static List<NavRouteStep> ThreeStepNorthboundRoute() => new()
    {
        Step(BaseLatitude + 0.001, BaseLongitude, 111, "Head north on Elm St"),
        Step(BaseLatitude + 0.002, BaseLongitude, 111, "Turn left onto County Rd 7", "turn-left"),
        Step(BaseLatitude + 0.003, BaseLongitude, 111, "Destination will be on the right")
    };

    [Fact]
    public void Update_WithNoRoute_ReturnsEmptySnapshot()
    {
        var tracker = new RouteProgressTracker();

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.False(tracker.HasRoute);
        Assert.False(snapshot.HasGuidance);
        Assert.Null(snapshot.NextManeuver);
        Assert.Equal(0, snapshot.MetersRemainingToDestination);
    }

    [Fact]
    public void SetSteps_WithNull_ClearsRouteWithoutThrowing()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());

        tracker.SetSteps(null);

        Assert.False(tracker.HasRoute);
        Assert.Null(tracker.Update(BaseLatitude, BaseLongitude).NextManeuver);
    }

    [Fact]
    public void Update_AtRouteStart_AnnouncesTheUpcomingTurnNotTheDepartureStep()
    {
        // The off-by-one guard: while driving step 0 ("Head north on Elm St")
        // the driver is approaching step 1's turn, and that is what the banner
        // must name.
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.NotNull(snapshot.NextManeuver);
        Assert.Equal("Turn left onto County Rd 7", snapshot.NextManeuver!.Instruction);
        Assert.Equal(0, tracker.CurrentStepIndex);

        // ~111 m to the turn at the end of step 0, and ~333 m of route in total.
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
        Assert.Equal(0, tracker.CurrentStepIndex);
    }

    [Fact]
    public void Update_OnReachingStepEnd_AdvancesToTheFollowingManeuver()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(ThreeStepNorthboundRoute());
        tracker.Update(BaseLatitude, BaseLongitude);

        // Sitting on the turn at the end of step 0; now driving step 1, whose
        // next event is the arrival described by step 2.
        var snapshot = tracker.Update(BaseLatitude + 0.001, BaseLongitude);

        Assert.Equal(1, tracker.CurrentStepIndex);
        Assert.Equal("Destination will be on the right", snapshot.NextManeuver!.Instruction);
    }

    [Fact]
    public void Update_WhenOnlyStraightStepsRemain_ReportsFinalApproach()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(new List<NavRouteStep>
        {
            Step(BaseLatitude + 0.001, BaseLongitude, 111, "Head north on Elm St"),
            Step(BaseLatitude + 0.002, BaseLongitude, 111, "Continue straight", "straight")
        });

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.True(snapshot.IsFinalApproach);
        Assert.Null(snapshot.NextManeuver);
        Assert.True(snapshot.HasGuidance);

        // On final approach the countdown targets the destination itself.
        Assert.Equal(snapshot.MetersRemainingToDestination, snapshot.MetersToNextManeuver, 3);
    }

    [Fact]
    public void Update_SkipsStraightStepsWhenChoosingTheManeuverToAnnounce()
    {
        // "Continue straight" between two turns is scaffolding, not an
        // instruction — the driver wants the turn beyond it.
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(new List<NavRouteStep>
        {
            Step(BaseLatitude + 0.001, BaseLongitude, 111, "Head north on Elm St"),
            Step(BaseLatitude + 0.002, BaseLongitude, 111, "Continue straight", "straight"),
            Step(BaseLatitude + 0.003, BaseLongitude, 111, "Turn right onto Hwy 169", "turn-right")
        });

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.Equal("Turn right onto Hwy 169", snapshot.NextManeuver!.Instruction);
    }

    [Fact]
    public void Update_CountsSkippedStraightStepsIntoTheDistanceToTheManeuver()
    {
        // The filtering must not shorten the countdown. Distance to the turn is
        // the ~111 m of step 0 plus the whole ~111 m straight step in between.
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(new List<NavRouteStep>
        {
            Step(BaseLatitude + 0.001, BaseLongitude, 111, "Head north on Elm St"),
            Step(BaseLatitude + 0.002, BaseLongitude, 111, "Continue straight", "straight"),
            Step(BaseLatitude + 0.003, BaseLongitude, 111, "Turn right onto Hwy 169", "turn-right")
        });

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.InRange(snapshot.MetersToNextManeuver, 210, 235);
    }

    [Fact]
    public void Update_KeepsRoadNameChangesWithNoManeuverToken()
    {
        // Google leaves Maneuver blank for plain "Continue onto <road>" steps
        // where the road changes name without a turn. Those are exactly the
        // cues a driver wants, so they must survive the filter.
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(new List<NavRouteStep>
        {
            Step(BaseLatitude + 0.001, BaseLongitude, 111, "Head north on Elm St"),
            Step(BaseLatitude + 0.002, BaseLongitude, 111, "Continue onto County Rd 7"),
            Step(BaseLatitude + 0.003, BaseLongitude, 111, "Turn right onto Hwy 169", "turn-right")
        });

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.Equal("Continue onto County Rd 7", snapshot.NextManeuver!.Instruction);
    }

    [Fact]
    public void Update_RemainingDistanceIncludesFilteredSteps()
    {
        // Whatever the banner shows, the distance to the stop spans every
        // remaining step. Three ~111 m steps means ~333 m regardless of how
        // many are filtered out of the display.
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(new List<NavRouteStep>
        {
            Step(BaseLatitude + 0.001, BaseLongitude, 111, "Head north on Elm St"),
            Step(BaseLatitude + 0.002, BaseLongitude, 111, "Continue straight", "straight"),
            Step(BaseLatitude + 0.003, BaseLongitude, 111, "Continue straight", "straight")
        });

        var snapshot = tracker.Update(BaseLatitude, BaseLongitude);

        Assert.InRange(snapshot.MetersRemainingToDestination, 320, 350);
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
        // reappears at the end of step 1 having never reported a position at
        // the end of step 0.
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
        Assert.Null(snapshot.NextManeuver);
    }

    [Fact]
    public void Update_OnSingleStepRoute_GoesStraightToFinalApproach()
    {
        var tracker = new RouteProgressTracker();
        tracker.SetSteps(new List<NavRouteStep>
        {
            Step(BaseLatitude + 0.001, BaseLongitude, 111, "Head north on Elm St")
        });

        var beforeArrival = tracker.Update(BaseLatitude, BaseLongitude);
        var afterArrival = tracker.Update(BaseLatitude + 0.001, BaseLongitude);

        Assert.True(beforeArrival.IsFinalApproach);
        Assert.Null(beforeArrival.NextManeuver);
        Assert.True(afterArrival.HasArrived);
    }

    [Fact]
    public void SetSteps_AfterProgress_RewindsToFirstStep()
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
