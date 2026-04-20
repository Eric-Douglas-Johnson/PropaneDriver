using System.Diagnostics;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class GeoFenceService
    {
        private const double METERS_PER_FOOT = 0.3048;
        private const double MIN_FENCE_RADIUS_FT = 25;
        private const double MAX_FENCE_RADIUS_FT = 2000;

        public double FenceRadiusMeters { get; private set; } = 200 * METERS_PER_FOOT; // 200 feet default
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
        private readonly DeliveryTimeApiService _apiService;
        private readonly DeliveryApiService _deliveryApi;

        private DeliveryDto? _activeDelivery;
        private bool _lastCheckWasInsideGeoFence;
        private readonly Stopwatch _timer = new();

        public event Action<GeoFenceEventArgs>? OnFenceStatusChanged;
        public event Action<double>? OnTimerTick;
        public event Action<SaveDeliveryTimeResult>? OnSaveResult;
        public event Action<DeliveryDto>? OnDeliveryCompleted;

        public bool IsInsideFence => _lastCheckWasInsideGeoFence;
        public double ElapsedSeconds => _timer.Elapsed.TotalSeconds;
        public bool IsMonitoring => _activeDelivery != null;

        public GeoFenceService(GeolocationService geolocationService, DeliveryTimeApiService apiService, DeliveryApiService deliveryApi)
        {
            _geolocationService = geolocationService;
            _apiService = apiService;
            _deliveryApi = deliveryApi;
            _geolocationService.OnPositionChanged += HandlePositionChanged;
        }

        public async Task SetTargetAsync(DeliveryDto? delivery)
        {
            _activeDelivery = delivery;
            _lastCheckWasInsideGeoFence = false;
            _timer.Reset();
        }

        // Backwards-compatible sync wrapper
        public void SetTarget(DeliveryDto? delivery) => _ = SetTargetAsync(delivery);

        private async void HandlePositionChanged(double latitude, double longitude, double accuracy)
        {
            try
            {
                if (_activeDelivery == null)
                {
                    await ErrorLogService.LogErrorAsync("GeoFenceService", "HandlePositionChanged called with no active delivery");
                    return;
                }

                if (!_activeDelivery.Location.HasCoordinates)
                {
                    await ErrorLogService.LogErrorAsync("GeoFenceService", $"Delivery '{_activeDelivery.CustomerName}' has no GPS coordinates");
                    return;
                }

                var distance = HaversineDistance(latitude, longitude, _activeDelivery.Location.Latitude,
                    _activeDelivery.Location.Longitude);

                var insideGeoFence = distance <= FenceRadiusMeters;

                if (insideGeoFence && !_lastCheckWasInsideGeoFence)
                {
                    _lastCheckWasInsideGeoFence = true;
                    _timer.Restart();
                }
                else if (!insideGeoFence && _lastCheckWasInsideGeoFence)
                {
                    await StopTimerAndSaveAsync();
                }

                if (_lastCheckWasInsideGeoFence)
                {
                    OnTimerTick?.Invoke(_timer.Elapsed.TotalSeconds);
                }

                OnFenceStatusChanged?.Invoke(new GeoFenceEventArgs
                {
                    DeliveryId = _activeDelivery.Id,
                    Address = _activeDelivery.Location.FullAddress,
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

        private async Task StopTimerAndSaveAsync()
        {
            _timer.Stop();

            if (_activeDelivery == null)
            {
                await ErrorLogService.LogErrorAsync("GeoFenceService.StopTimerAndSaveAsync", "_currentDelivery is null");
            }
            else if (_timer.Elapsed.TotalSeconds <= 0)
            {
                await ErrorLogService.LogErrorAsync("GeoFenceService.StopTimerAndSaveAsync", "_timer.Elapsed.TotalSeconds <= 0");
            }
            else
            {
                var dto = new DeliveryTimeDto
                {
                    DeliveryId = _activeDelivery.Id,
                    Street = _activeDelivery.Location.Street,
                    City = _activeDelivery.Location.City,
                    State = _activeDelivery.Location.State,
                    ZipCode = _activeDelivery.Location.ZipCode,
                    Latitude = _activeDelivery.Location.Latitude,
                    Longitude = _activeDelivery.Location.Longitude,
                    TimeIntervalSeconds = _timer.Elapsed.TotalSeconds
                };

                try
                {
                    var result = await _apiService.SaveDeliveryTimeAsync(dto);
                    OnSaveResult?.Invoke(result);
                }
                catch (Exception ex)
                {
                    await ErrorLogService   .LogErrorAsync("GeoFenceService", $"StopTimerAndSaveAsync failed: {ex.Message}");
                    OnSaveResult?.Invoke(new SaveDeliveryTimeResult { Success = false, ErrorMessage = ex.Message });
                }

                // Mark the delivery Complete (status = 2) if it isn't already.
                // This lives here (not in the page) so it still fires when the
                // user is on the Navigation page or anywhere else.
                if (_activeDelivery.Status != 2)
                {
                    var completed = _activeDelivery;
                    completed.Status = 2;

                    try
                    {
                        await _deliveryApi.UpdateStatusAsync(completed.Id, 2);
                    }
                    catch (Exception ex)
                    {
                        await ErrorLogService.LogErrorAsync("GeoFenceService", $"UpdateStatusAsync failed: {ex.Message}");
                    }

                    OnDeliveryCompleted?.Invoke(completed);

                    // Clear the active delivery so we don't re-complete it on
                    // the next fence crossing. The next target is set by the
                    // page when it handles OnDeliveryCompleted.
                    _activeDelivery = null;
                }
            }

            _lastCheckWasInsideGeoFence = false;
            _timer.Reset();
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
