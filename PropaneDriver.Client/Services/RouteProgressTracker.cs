using System.Text.Json.Serialization;

namespace PropaneDriver.Client.Services
{
    // One step from a Google Directions leg. Populated by navMapDrawRoute in
    // index.html — the property names here must match the JSON keys that
    // function resolves with.
    //
    // Note Google's indexing: a step's Instruction describes the maneuver
    // performed at its START, and EndLatitude/EndLongitude is the point where
    // the NEXT step's maneuver happens. So while the driver is travelling
    // step i, the turn they are approaching is step i+1.
    public class NavRouteStep
    {
        [JsonPropertyName("instruction")]
        public string Instruction { get; set; } = string.Empty;

        // Google's maneuver token ("turn-left", "roundabout-right", "fork-left",
        // …). Empty when Google doesn't classify the step — typically the
        // departure step and plain "Continue onto <road>" road-name changes.
        [JsonPropertyName("maneuver")]
        public string Maneuver { get; set; } = string.Empty;

        // Length of this step.
        [JsonPropertyName("distanceMeters")]
        public double DistanceMeters { get; set; }

        [JsonPropertyName("endLat")]
        public double EndLatitude { get; set; }

        [JsonPropertyName("endLng")]
        public double EndLongitude { get; set; }
    }

    // What the Navigation page renders after each GPS fix.
    public class RouteProgressSnapshot
    {
        // The maneuver the driver is approaching. Null on the final approach
        // (nothing left to do but arrive) and once arrival has happened.
        public NavRouteStep? NextManeuver { get; set; }

        // True when there is no further maneuver between here and the
        // destination — the banner should say "arriving" rather than name a turn.
        public bool IsFinalApproach { get; set; }

        public double MetersToNextManeuver { get; set; }

        public double MetersRemainingToDestination { get; set; }

        // True once the driver has passed the end of the final step.
        public bool HasArrived { get; set; }

        // Whether there is anything to draw at all.
        public bool HasGuidance => NextManeuver is not null || IsFinalApproach || HasArrived;
    }

    // Tracks which maneuver of a drawn route the driver is approaching, and how
    // far is left — both to that maneuver and to the destination.
    //
    // Deliberately a plain class: no DI registration, no JS interop, no HTTP. All
    // it does is arithmetic over a step list and a coordinate, which keeps it
    // unit-testable (see PropaneDriver.Tests/RouteProgressTrackerTests.cs).
    public class RouteProgressTracker
    {
        // How close the driver must get to a step's end point before we treat
        // that leg as done. Loose enough to absorb consumer-GPS error and the
        // offset between the road centreline and Google's turn point; tight
        // enough not to skip closely-spaced turns in town.
        private const double StepAdvanceThresholdMeters = 30.0;

        // How many steps ahead we're willing to jump in a single update. GPS
        // fixes arrive about once a second, so under normal driving the index
        // moves by one; this only matters when fixes are dropped (tunnel, dead
        // zone, backgrounded tab) and the driver reappears several turns along.
        private const int MaximumStepsToSkipPerUpdate = 5;

        // Google's token for "carry on through this intersection without
        // turning". These steps exist to break a road up at junctions, not to
        // tell the driver to do anything, so they never become the banner's
        // headline maneuver. Their distance is still counted — see
        // FindNextActionableManeuverIndex.
        private const string NonActionableManeuver = "straight";

        private List<NavRouteStep> _steps = new();
        private int _currentStepIndex;

        public bool HasRoute => _steps.Count > 0;

        public int CurrentStepIndex => _currentStepIndex;

        // Loads a freshly-drawn route and rewinds to its first step.
        public void SetSteps(IReadOnlyList<NavRouteStep>? steps)
        {
            _steps = steps is null
                ? new List<NavRouteStep>()
                : steps.ToList();
            _currentStepIndex = 0;
        }

        public void Clear() => SetSteps(null);

        // Recomputes progress for the driver's current position.
        //
        // The step index only ever moves forward. Picking the nearest step end
        // instead would break on out-and-backs and on routes that cross the same
        // intersection twice — both routine on a rural propane route, where a
        // driver regularly comes back out the way they went in.
        public RouteProgressSnapshot Update(double latitude, double longitude)
        {
            if (_steps.Count == 0)
            {
                return new RouteProgressSnapshot();
            }

            AdvancePastCompletedSteps(latitude, longitude);

            if (_currentStepIndex >= _steps.Count)
            {
                return new RouteProgressSnapshot { HasArrived = true };
            }

            var currentStep = _steps[_currentStepIndex];

            // Distance to the end of the step being driven. Every other figure
            // below is this plus whole steps, so it's computed once.
            var metersToCurrentStepEnd = GeoFenceService.HaversineDistance(
                latitude, longitude, currentStep.EndLatitude, currentStep.EndLongitude);

            // Remaining distance is computed from where the driver actually is,
            // not read off the leg total — a stored total never counts down. It
            // spans every remaining step, including ones filtered out of the
            // banner, so filtering can't corrupt the distance to the stop.
            var metersRemainingToDestination = metersToCurrentStepEnd
                + SumStepDistances(_currentStepIndex + 1, _steps.Count - 1);

            var nextManeuverIndex = FindNextActionableManeuverIndex(_currentStepIndex);

            if (nextManeuverIndex is null)
            {
                return new RouteProgressSnapshot
                {
                    IsFinalApproach = true,
                    MetersToNextManeuver = metersRemainingToDestination,
                    MetersRemainingToDestination = metersRemainingToDestination
                };
            }

            // The maneuver for step j happens at step j-1's end point, so the
            // distance to it is the distance to the current step's end plus
            // every whole step in between.
            var metersToNextManeuver = metersToCurrentStepEnd
                + SumStepDistances(_currentStepIndex + 1, nextManeuverIndex.Value - 1);

            return new RouteProgressSnapshot
            {
                NextManeuver = _steps[nextManeuverIndex.Value],
                MetersToNextManeuver = metersToNextManeuver,
                MetersRemainingToDestination = metersRemainingToDestination
            };
        }

        // Finds the next step whose maneuver is worth announcing, searching
        // strictly ahead of the step being driven. Returns null when only
        // non-actionable steps remain, i.e. the driver is on final approach.
        //
        // Steps skipped here are skipped for DISPLAY only. Their distance is
        // still summed into the distance-to-maneuver figure, so a "continue
        // straight" between two turns lengthens the countdown to the next turn
        // rather than disappearing from it.
        private int? FindNextActionableManeuverIndex(int fromStepIndex)
        {
            for (var candidateIndex = fromStepIndex + 1; candidateIndex < _steps.Count; candidateIndex++)
            {
                if (IsActionableManeuver(_steps[candidateIndex]))
                {
                    return candidateIndex;
                }
            }

            return null;
        }

        // A step is worth announcing if it asks the driver to do something: turn,
        // merge, take a ramp, pick a fork, enter a roundabout.
        //
        // An empty maneuver counts as actionable on purpose. Google leaves it
        // blank for the departure step ("Head north on Elm St") and for plain
        // "Continue onto <road>" steps where the road changes name without a
        // turn. The departure step is never a candidate here — the search starts
        // strictly ahead of the current step — so in practice a blank maneuver
        // means a road change, which is exactly the kind of cue a driver wants.
        private static bool IsActionableManeuver(NavRouteStep step)
            => !string.Equals(step.Maneuver, NonActionableManeuver, StringComparison.OrdinalIgnoreCase);

        // Inclusive on both ends; returns 0 for an empty or inverted range.
        private double SumStepDistances(int firstStepIndex, int lastStepIndex)
        {
            var total = 0.0;
            for (var stepIndex = firstStepIndex; stepIndex <= lastStepIndex; stepIndex++)
            {
                total += _steps[stepIndex].DistanceMeters;
            }
            return total;
        }

        // Walks the index forward past every step whose end point the driver is
        // already standing on. Scans a bounded window rather than stopping at the
        // first miss so a gap in the GPS stream can't wedge the tracker on a step
        // the driver drove through several turns ago.
        private void AdvancePastCompletedSteps(double latitude, double longitude)
        {
            var furthestCompletedStepIndex = -1;
            var lastStepToConsider = Math.Min(
                _currentStepIndex + MaximumStepsToSkipPerUpdate,
                _steps.Count - 1);

            for (var candidateIndex = _currentStepIndex; candidateIndex <= lastStepToConsider; candidateIndex++)
            {
                var candidate = _steps[candidateIndex];
                var distanceToCandidateEnd = GeoFenceService.HaversineDistance(
                    latitude, longitude, candidate.EndLatitude, candidate.EndLongitude);

                if (distanceToCandidateEnd <= StepAdvanceThresholdMeters)
                {
                    furthestCompletedStepIndex = candidateIndex;
                }
            }

            if (furthestCompletedStepIndex >= 0)
            {
                _currentStepIndex = furthestCompletedStepIndex + 1;
            }
        }
    }
}
