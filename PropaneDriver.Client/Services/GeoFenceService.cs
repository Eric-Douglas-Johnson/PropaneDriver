using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.Interfaces;

namespace PropaneDriver.Client.Services
{
    public class GeoFenceService
    {
        private const double METERS_PER_FOOT = 0.3048;
        private const double MIN_FENCE_RADIUS_FT = 25;
        private const double MAX_FENCE_RADIUS_FT = 2000;

        public double FenceRadiusMeters { get; private set; } = 150 * METERS_PER_FOOT; // 150 feet default
        public double FenceRadiusFeet => FenceRadiusMeters / METERS_PER_FOOT;

        public void AdjustFenceRadiusFeet(double deltaFeet)
        {
            var newFeet = Math.Clamp(FenceRadiusFeet + deltaFeet, MIN_FENCE_RADIUS_FT, MAX_FENCE_RADIUS_FT);
            FenceRadiusMeters = newFeet * METERS_PER_FOOT;
        }

        public void SetFenceRadiusFeet(double feet)
        {
            var clamped = Math.Clamp(feet, MIN_FENCE_RADIUS_FT, MAX_FENCE_RADIUS_FT);
            FenceRadiusMeters = clamped * METERS_PER_FOOT;
        }

        private readonly GeolocationService _geolocationService;
        private readonly DeliveryTimerService _deliveryTimerService;
        private readonly DeliveryCompletionService _deliveryCompletion;

        private IDelivery? _activeDelivery;
        private bool _lastCheckWasInsideGeoFence;

        public event Action<GeoFenceEventArgs>? OnFenceStatusChanged;

        public bool IsInsideFence => _lastCheckWasInsideGeoFence;
        public bool IsMonitoring => _activeDelivery != null;

        public GeoFenceService(
            GeolocationService geolocationService,
            DeliveryTimerService deliveryTimerService,
            DeliveryCompletionService deliveryCompletion)
        {
            _geolocationService = geolocationService;
            _deliveryTimerService = deliveryTimerService;
            _deliveryCompletion = deliveryCompletion;
            _geolocationService.OnPositionChanged += HandlePositionChanged;
        }

        // Arms the fence for a delivery, or disarms it when passed null. The
        // caller decides which stops get geofenced; this service watches
        // whatever it is handed.
        public void SetTarget(IDelivery? delivery)
        {
            _activeDelivery = delivery;

            // A timer already running for this stop means the driver was inside
            // the fence before a page reload. Relies on the caller having
            // restored persisted timers first.
            _lastCheckWasInsideGeoFence = delivery is not null && _deliveryTimerService.IsRunningFor(delivery.Id);
        }

        private async void HandlePositionChanged(double latitude, double longitude, double accuracy)
        {
            try
            {
                // GPS updates fire continuously while the service is watching.
                // "No active delivery" is just the idle state — the driver hasn't
                // selected one yet, or finished the whole route. Bail silently;
                // logging this as an error flooded ErrorLog with one row per fix.
                if (_activeDelivery == null)
                {
                    return;
                }

                // Same story for missing coordinates: not an error per fix, only
                // worth noting once per delivery. Let the Admin page surface
                // missing-GPS as a data-quality issue instead.
                if (!_activeDelivery.Address.HasCoordinates)
                {
                    return;
                }

                var distance = HaversineDistance(latitude, longitude, _activeDelivery.Address.Latitude,
                    _activeDelivery.Address.Longitude);

                var insideGeoFence = distance <= FenceRadiusMeters;

                if (insideGeoFence && !_lastCheckWasInsideGeoFence)
                {
                    _lastCheckWasInsideGeoFence = true;

                    try
                    {
                        await _deliveryTimerService.StartAsync(_activeDelivery.Id);
                    }
                    catch (ArgumentException ex)
                    {
                        await ErrorLogService.LogErrorAsync(
                            "GeoFenceService.HandlePositionChanged", $"Starting fence timer failed: {ex.Message}");
                    }
                }
                else if (!insideGeoFence && _lastCheckWasInsideGeoFence)
                {
                    await CompleteOnFenceExitAsync();
                    return;
                }

                OnFenceStatusChanged?.Invoke(new GeoFenceEventArgs
                {
                    DeliveryId = _activeDelivery.Id,
                    Address = _activeDelivery.Address.FullAddress,
                    Latitude = latitude,
                    Longitude = longitude,
                    IsInsideFence = _lastCheckWasInsideGeoFence,
                    DistanceMeters = distance
                });
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync("GeoFenceService", $"HandlePositionChanged failed: {ex.Message}");
            }
        }

        // Leaving the fence ends the stop: stop the clock and hand the elapsed
        // time to the completion pipeline.
        private async Task CompleteOnFenceExitAsync()
        {
            var departedDelivery = _activeDelivery;
            _lastCheckWasInsideGeoFence = false;

            var rawElapsedSeconds = await _deliveryTimerService.StopAsync();
            var wasCompleted = await _deliveryCompletion.CompleteAsync(departedDelivery, rawElapsedSeconds);

            // Disarm only once the stop is actually done, so a delivery that
            // failed to complete can still be retried on the next crossing.
            if (wasCompleted)
            {
                _activeDelivery = null;
            }
        }

        /// <summary>
        /// Haversine formula to calculate the distance in meters between two GPS coordinates.
        /// </summary>
        public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000; // Earth radius in meters

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    }
}
