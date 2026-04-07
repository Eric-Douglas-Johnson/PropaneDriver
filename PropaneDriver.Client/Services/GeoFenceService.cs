using System.Diagnostics;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.Services
{
    public class GeoFenceService
    {
        private const double FenceRadiusMeters = 30.48; // 100 feet
        private readonly GeolocationService _geolocationService;
        private readonly DeliveryTimeApiService _apiService;

        private DeliveryDto? _currentTarget;
        private bool _isInsideFence;
        private readonly Stopwatch _timer = new();

        public event Action<GeoFenceEventArgs>? OnFenceStatusChanged;
        public event Action<double>? OnTimerTick;

        public bool IsInsideFence => _isInsideFence;
        public double ElapsedSeconds => _timer.Elapsed.TotalSeconds;
        public bool IsMonitoring => _currentTarget != null;

        public GeoFenceService(GeolocationService geolocationService, DeliveryTimeApiService apiService)
        {
            _geolocationService = geolocationService;
            _apiService = apiService;
            _geolocationService.OnPositionChanged += HandlePositionChanged;
        }

        public void SetTarget(DeliveryDto? delivery)
        {
            // If we were inside a fence for a previous target, stop the timer
            if (_isInsideFence && _currentTarget != null)
            {
                StopTimerAndSave();
            }

            _currentTarget = delivery;
            _isInsideFence = false;
            _timer.Reset();
        }

        private void HandlePositionChanged(double latitude, double longitude, double accuracy)
        {
            if (_currentTarget == null || !_currentTarget.Location.HasCoordinates) return;

            var distance = HaversineDistance(
                latitude, longitude,
                _currentTarget.Location.Latitude,
                _currentTarget.Location.Longitude);

            var nowInside = distance <= FenceRadiusMeters;

            if (nowInside && !_isInsideFence)
            {
                // Entered the geofence
                _isInsideFence = true;
                _timer.Restart();
            }
            else if (!nowInside && _isInsideFence)
            {
                // Left the geofence
                StopTimerAndSave();
            }

            if (_isInsideFence)
            {
                OnTimerTick?.Invoke(_timer.Elapsed.TotalSeconds);
            }

            OnFenceStatusChanged?.Invoke(new GeoFenceEventArgs
            {
                DeliveryId = _currentTarget.Id,
                Address = _currentTarget.Location.FullAddress,
                Latitude = latitude,
                Longitude = longitude,
                IsInsideFence = _isInsideFence,
                DistanceMeters = distance
            });
        }

        private void StopTimerAndSave()
        {
            _timer.Stop();

            if (_currentTarget != null && _timer.Elapsed.TotalSeconds > 0)
            {
                var dto = new DeliveryTimeDto
                {
                    DeliveryId = _currentTarget.Id,
                    Address = _currentTarget.Location.FullAddress,
                    Latitude = _currentTarget.Location.Latitude,
                    Longitude = _currentTarget.Location.Longitude,
                    TimeIntervalSeconds = _timer.Elapsed.TotalSeconds
                };

                // Fire and forget — save delivery time to server
                _ = _apiService.SaveDeliveryTimeAsync(dto);
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
