using System.Text.Json.Serialization;

namespace PropaneDriver.Client.Services
{
    // One maneuver from a Google Directions leg. Populated by navMapDrawRoute
    // in index.html — the property names here must match the JSON keys that
    // function resolves with.
    public class NavRouteStep
    {
        [JsonPropertyName("instruction")]
        public string Instruction { get; set; } = string.Empty;

        // Google's maneuver token ("turn-left", "roundabout-right", "fork-left",
        // …). Empty when Google doesn't classify the step, which is common for
        // the first and last step of a leg.
        [JsonPropertyName("maneuver")]
        public string Maneuver { get; set; } = string.Empty;

        // Length of this step, not the distance remaining within it.
        [JsonPropertyName("distanceMeters")]
        public double DistanceMeters { get; set; }

        // The point at which this step's maneuver happens — i.e. the corner the
        // driver turns at. Distance-to-turn is measured against this.
        [JsonPropertyName("endLat")]
        public double EndLatitude { get; set; }

        [JsonPropertyName("endLng")]
        public double EndLongitude { get; set; }
    }

    // What the Navigation page renders after each GPS fix.
    public class RouteProgressSnapshot
    {
        // Null when there is no route loaded or the driver has run off the end
        // of the step list. The page renders no banner in that case.
        public NavRouteStep? CurrentStep { get; set; }

        public double MetersToNextManeuver { get; set; }

        public double MetersRemainingToDestination { get; set; }

        // True once the driver has passed the end of the final step.
        public bool HasArrived { get; set; }
    }

    // Tracks which maneuver of a drawn route the driver is currently working on,
    // and how far is left — both to that maneuver and to the destination.
    //
    // Deliberately a plain class: no DI registration, no JS interop, no HTTP. All
    // it does is arithmetic over a step list and a coordinate, which keeps it
    // unit-testable (see PropaneDriver.Tests/RouteProgressTrackerTests.cs).
    public class RouteProgressTracker
    {
        // How close the driver must get to a step's end point before we treat
        // that maneuver as done. Loose enough to absorb consumer-GPS error and
        // the offset between the road centreline and Google's turn point;
        // tight enough not to skip closely-spaced turns in town.
        private const double StepAdvanceThresholdMeters = 30.0;

        // How many steps ahead we're willing to jump in a single update. GPS
        // fixes arrive about once a second, so under normal driving the index
        // moves by one; this only matters when fixes are dropped (tunnel, dead
        // zone, backgrounded tab) and the driver reappears several turns along.
        private const int MaximumStepsToSkipPerUpdate = 5;

        private List<NavRouteStep> _steps = new();
        private int _currentStepIndex;

        public bool HasRoute => _steps.Count > 0;

        public int CurrentStepIndex => _currentStepIndex;

        // Loads a freshly-drawn route and rewinds to its first maneuver.
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

            var metersToNextManeuver = GeoFenceService.HaversineDistance(
                latitude, longitude, currentStep.EndLatitude, currentStep.EndLongitude);

            // Remaining distance is computed from where the driver actually is,
            // not read off the leg total — a stored total never counts down.
            var metersRemainingToDestination = metersToNextManeuver;
            for (var laterStepIndex = _currentStepIndex + 1; laterStepIndex < _steps.Count; laterStepIndex++)
            {
                metersRemainingToDestination += _steps[laterStepIndex].DistanceMeters;
            }

            return new RouteProgressSnapshot
            {
                CurrentStep = currentStep,
                MetersToNextManeuver = metersToNextManeuver,
                MetersRemainingToDestination = metersRemainingToDestination
            };
        }

        // Walks the index forward past every step whose turn point the driver is
        // already standing on. Scans a bounded window rather than stopping at the
        // first miss so a gap in the GPS stream can't wedge the tracker on a
        // maneuver the driver drove through several turns ago.
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
