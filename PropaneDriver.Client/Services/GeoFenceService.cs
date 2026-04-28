using PropaneDriver.Client.Authentication;
using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.Interfaces;

namespace PropaneDriver.Client.Services
{
    public class GeoFenceService
    {
        // localStorage key for the in-flight fence-entry instant. Persisting
        // this lets us resume the timer across page reloads instead of
        // restarting from zero on the next GPS update.
        private const string TimerStorageKey = "propanedriver.geofence.activeTimer";

        private class StoredTimerState
        {
            public string DeliveryId { get; set; } = string.Empty;
            public DateTime EnteredAtUtc { get; set; }
        }

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
        private readonly DeliveryTimeApiService _apiService;
        private readonly DeliveryApiService _deliveryApi;
        private readonly BrowserStorageService _storage;

        private IDelivery? _activeDelivery;
        private bool _lastCheckWasInsideGeoFence;
        // Wall-clock timestamp the driver crossed into the fence. Wall-clock
        // (not Stopwatch) so the elapsed time keeps advancing through page
        // reloads — Stopwatch is in-memory only and a refresh would zero it.
        private DateTime? _enteredFenceAtUtc;

        public event Action<GeoFenceEventArgs>? OnFenceStatusChanged;
        public event Action<double>? OnTimerTick;
        public event Action<SaveDeliveryTimeResult>? OnSaveResult;
        public event Action<IDelivery, double>? OnDeliveryCompleted;

        public bool IsInsideFence => _lastCheckWasInsideGeoFence;
        public double ElapsedSeconds => _enteredFenceAtUtc is { } start
            ? (DateTime.UtcNow - start).TotalSeconds
            : 0;
        public bool IsMonitoring => _activeDelivery != null;

        public GeoFenceService(
            GeolocationService geolocationService,
            DeliveryTimeApiService apiService,
            DeliveryApiService deliveryApi,
            BrowserStorageService storage)
        {
            _geolocationService = geolocationService;
            _apiService = apiService;
            _deliveryApi = deliveryApi;
            _storage = storage;
            _geolocationService.OnPositionChanged += HandlePositionChanged;
        }

        public async Task SetTargetAsync(IDelivery? delivery)
        {
            _activeDelivery = delivery;
            _lastCheckWasInsideGeoFence = false;
            _enteredFenceAtUtc = null;

            // If we previously persisted a fence-entry instant for THIS
            // delivery (i.e. the page just reloaded mid-delivery), restore
            // it so the timer resumes from the original entry time. Stale
            // state for a different (or completed) delivery gets cleared
            // so it can't poison the next timer.
            StoredTimerState? stored = null;
            try
            {
                stored = await _storage.GetFromStorage<StoredTimerState>(TimerStorageKey);
            }
            catch
            {
                // Corrupted JSON or schema drift — fall through to clear.
            }

            if (stored is not null
                && delivery is not null
                && !string.IsNullOrEmpty(stored.DeliveryId)
                && stored.DeliveryId == delivery.Id)
            {
                _enteredFenceAtUtc = stored.EnteredAtUtc;
                _lastCheckWasInsideGeoFence = true;
            }
            else if (stored is not null)
            {
                try { await _storage.RemoveFromStorage(TimerStorageKey); } catch { }
            }
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
                if (!_activeDelivery.Location.HasCoordinates)
                {
                    return;
                }

                var distance = HaversineDistance(latitude, longitude, _activeDelivery.Location.Latitude,
                    _activeDelivery.Location.Longitude);

                var insideGeoFence = distance <= FenceRadiusMeters;

                if (insideGeoFence && !_lastCheckWasInsideGeoFence)
                {
                    _lastCheckWasInsideGeoFence = true;
                    _enteredFenceAtUtc = DateTime.UtcNow;
                    try
                    {
                        await _storage.SaveToStorageAsync(TimerStorageKey, new StoredTimerState
                        {
                            DeliveryId = _activeDelivery.Id,
                            EnteredAtUtc = _enteredFenceAtUtc.Value
                        });
                    }
                    catch (Exception ex)
                    {
                        await ErrorLogService.LogErrorAsync(
                            "GeoFenceService", $"Persisting fence entry failed: {ex.Message}");
                    }
                }
                else if (!insideGeoFence && _lastCheckWasInsideGeoFence)
                {
                    await StopTimerAndSaveAsync();
                }

                if (_lastCheckWasInsideGeoFence)
                {
                    OnTimerTick?.Invoke(ElapsedSeconds);
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
            // Capture elapsed BEFORE we clear _enteredFenceAtUtc — once it's
            // null, ElapsedSeconds returns 0 and we'd save a zeroed time.
            var rawElapsedSeconds = ElapsedSeconds;
            _enteredFenceAtUtc = null;
            try { await _storage.RemoveFromStorage(TimerStorageKey); } catch { }

            if (_activeDelivery == null)
            {
                await ErrorLogService.LogErrorAsync("GeoFenceService.StopTimerAndSaveAsync", "_currentDelivery is null");
            }
            else if (rawElapsedSeconds <= 0)
            {
                await ErrorLogService.LogErrorAsync("GeoFenceService.StopTimerAndSaveAsync", "rawElapsedSeconds <= 0");
            }
            else
            {
                if (_activeDelivery.Location.Id == Guid.Empty)
                {
                    await ErrorLogService.LogErrorAsync(
                        "GeoFenceService", $"Delivery '{_activeDelivery.CustomerName}' has no AddressId — cannot save time");
                    return;
                }

                const double MinDeliverySeconds = 5 * 60;
                var elapsedSeconds = Math.Max(rawElapsedSeconds, MinDeliverySeconds);
                var dto = new DeliveryTimeDto
                {
                    DeliveryId = _activeDelivery.Id,
                    AddressId = _activeDelivery.Location.Id,
                    TimeIntervalSeconds = elapsedSeconds
                };

                try
                {
                    var result = await _apiService.SaveDeliveryTimeAsync(dto);
                    OnSaveResult?.Invoke(result);
                }
                catch (Exception ex)
                {
                    await ErrorLogService.LogErrorAsync("GeoFenceService", $"StopTimerAndSaveAsync failed: {ex.Message}");
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

                    OnDeliveryCompleted?.Invoke(completed, elapsedSeconds);

                    // Clear the active delivery so we don't re-complete it on
                    // the next fence crossing. The next target is set by the
                    // page when it handles OnDeliveryCompleted.
                    _activeDelivery = null;
                }
            }

            _lastCheckWasInsideGeoFence = false;
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
