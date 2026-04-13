using System.Diagnostics;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class GeoFenceService
    {
        private const double FENCE_RADIUS = 30.48; // 100 feet
        private readonly GeolocationService _geolocationService;
        private readonly DeliveryTimeApiService _apiService;
        private readonly ErrorLogService _errorLog;

        private DeliveryDto? _currentDelivery;
        private bool _isInsideFence;
        private readonly Stopwatch _timer = new();

        public event Action<GeoFenceEventArgs>? OnFenceStatusChanged;
        public event Action<double>? OnTimerTick;
        public event Action<SaveDeliveryTimeResult>? OnSaveResult;

        public bool IsInsideFence => _isInsideFence;
        public double ElapsedSeconds => _timer.Elapsed.TotalSeconds;
        public bool IsMonitoring => _currentDelivery != null;

        public GeoFenceService(GeolocationService geolocationService, DeliveryTimeApiService apiService, ErrorLogService errorLog)
        {
            _geolocationService = geolocationService;
            _apiService = apiService;
            _errorLog = errorLog;
            _geolocationService.OnPositionChanged += HandlePositionChanged;
        }

        public async Task SetTargetAsync(DeliveryDto? delivery)
        {
            // If we were inside a fence for a previous target, stop the timer
            if (_isInsideFence && _currentDelivery != null)
            {
                await StopTimerAndSaveAsync();
            }

            _currentDelivery = delivery;
            _isInsideFence = false;
            _timer.Reset();
        }

        // Backwards-compatible sync wrapper
        public void SetTarget(DeliveryDto? delivery) => _ = SetTargetAsync(delivery);

        /// <summary>
        /// Force a save of the current timer if inside the fence. Safe to call at any time.
        /// </summary>
        public async Task FlushAsync()
        {
            if (_isInsideFence && _currentDelivery != null)
            {
                await StopTimerAndSaveAsync();
            }
        }

        private async void HandlePositionChanged(double latitude, double longitude, double accuracy)
        {
            try
            {
                if (_currentDelivery == null)
                {
                    await _errorLog.LogErrorAsync("GeoFenceService", "HandlePositionChanged called with no current delivery");
                    return;
                }

                if (!_currentDelivery.Location.HasCoordinates)
                {
                    await _errorLog.LogErrorAsync("GeoFenceService", $"Delivery '{_currentDelivery.CustomerName}' has no GPS coordinates");
                    return;
                }

                var distance = HaversineDistance(latitude, longitude, _currentDelivery.Location.Latitude,
                    _currentDelivery.Location.Longitude);

                var nowInside = distance <= FENCE_RADIUS;

                if (nowInside && !_isInsideFence)
                {
                    _isInsideFence = true;
                    _timer.Restart();
                }
                else if (!nowInside && _isInsideFence)
                {
                    await StopTimerAndSaveAsync();
                }

                if (_isInsideFence)
                {
                    OnTimerTick?.Invoke(_timer.Elapsed.TotalSeconds);
                }

                OnFenceStatusChanged?.Invoke(new GeoFenceEventArgs
                {
                    DeliveryId = _currentDelivery.Id,
                    Address = _currentDelivery.Location.FullAddress,
                    Latitude = latitude,
                    Longitude = longitude,
                    IsInsideFence = _isInsideFence,
                    DistanceMeters = distance
                });
            }
            catch (Exception ex)
            {
                await _errorLog.LogErrorAsync("GeoFenceService", $"HandlePositionChanged failed: {ex.Message}");
            }
        }

        private async Task StopTimerAndSaveAsync()
        {
            _timer.Stop();

            if (_currentDelivery != null && _timer.Elapsed.TotalSeconds > 0)
            {
                var dto = new DeliveryTimeDto
                {
                    DeliveryId = _currentDelivery.Id,
                    Address = _currentDelivery.Location.FullAddress,
                    Latitude = _currentDelivery.Location.Latitude,
                    Longitude = _currentDelivery.Location.Longitude,
                    TimeIntervalSeconds = _timer.Elapsed.TotalSeconds
                };

                try
                {
                    var result = await _apiService.SaveDeliveryTimeAsync(dto);
                    OnSaveResult?.Invoke(result);
                }
                catch (Exception ex)
                {
                    await _errorLog.LogErrorAsync("GeoFenceService", $"StopTimerAndSaveAsync failed: {ex.Message}");
                    OnSaveResult?.Invoke(new SaveDeliveryTimeResult { Success = false, ErrorMessage = ex.Message });
                }
            }

            _isInsideFence = false;
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
