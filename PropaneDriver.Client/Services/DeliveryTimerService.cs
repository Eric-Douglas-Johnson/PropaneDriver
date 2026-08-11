
using PropaneDriver.Client.Authentication;
using PropaneDriver.Client.Classes;

namespace PropaneDriver.Client.Services
{
    // Owns delivery timing mechanics
    public class DeliveryTimerService : IDisposable
    {
        private const string RUNNING_TIMER_STORAGE_KEY = "propanedriver.deliveryTimer.running";

        private readonly BrowserStorageService _browserStorage;
        private RunningTimer? _runningTimer;
        private System.Threading.Timer? _tickTimer;

        public event Action<double>? OnTick;

        public DeliveryTimerService(BrowserStorageService browserStorage)
        {
            _browserStorage = browserStorage;
        }

        public string? RunningDeliveryId => _runningTimer?.DeliveryId;

        public bool IsAnyTimerRunning => _runningTimer is not null;

        public double ElapsedSecondsFor(string? deliveryId)
            => IsRunningFor(deliveryId)
                ? (DateTime.UtcNow - _runningTimer!.StartedAtUtc).TotalSeconds
                : 0;

        public bool IsRunningFor(string? deliveryId)
            => !string.IsNullOrEmpty(deliveryId) && _runningTimer?.DeliveryId == deliveryId;

        public async Task StartAsync(string deliveryId)
        {
            if (string.IsNullOrEmpty(deliveryId)) throw new ArgumentException("Timer could not be started: missing valid delivery Id.");

            _runningTimer = new RunningTimer
            {
                DeliveryId = deliveryId,
                StartedAtUtc = DateTime.UtcNow
            };

            await PersistRunningTimerAsync(_runningTimer);
            StartTickTimer();

            OnTick?.Invoke(ElapsedSecondsFor(deliveryId));
        }

        // Arg: runningTimer --> must not be null
        private async Task PersistRunningTimerAsync(RunningTimer runningTimer)
        {
            try
            {
                await _browserStorage.SaveToStorageAsync(RUNNING_TIMER_STORAGE_KEY, runningTimer);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryTimerService", $"Persisting timer start failed: {ex.Message}");
            }
        }

        private void StartTickTimer()
        {
            _tickTimer?.Dispose();

            _tickTimer = new System.Threading.Timer(
                _ => OnTick?.Invoke(ElapsedSecondsFor(_runningTimer?.DeliveryId)),
                state: null,
                dueTime: TimeSpan.FromSeconds(1),
                period: TimeSpan.FromSeconds(1));
        }

        public async Task<double> StopAsync()
        {
            if (_runningTimer is null) return 0;

            var elapsedSeconds = ElapsedSecondsFor(_runningTimer.DeliveryId);
            await ClearAsync();

            return elapsedSeconds;
        }

        public async Task ClearAsync()
        {
            StopTickTimer();
            _runningTimer = null;

            try
            {
                await _browserStorage.RemoveFromStorage(RUNNING_TIMER_STORAGE_KEY);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryTimerService", $"Clearing persisted timer failed: {ex.Message}");
            }
        }

        public async Task RestoreAsync()
        {
            RunningTimer? storedTimer = null;

            try
            {
                storedTimer = await _browserStorage.GetFromStorage<RunningTimer>(RUNNING_TIMER_STORAGE_KEY);
            }
            catch
            {
                // Corrupted JSON or schema drift — treat as nothing stored.
            }

            if (storedTimer is null || string.IsNullOrEmpty(storedTimer.DeliveryId))
            {
                StopTickTimer();
                _runningTimer = null;

                return;
            }

            _runningTimer = storedTimer;
            StartTickTimer();
        }

        public void Dispose() => StopTickTimer();

        private void StopTickTimer()
        {
            _tickTimer?.Dispose();
            _tickTimer = null;
        }
    }
}
