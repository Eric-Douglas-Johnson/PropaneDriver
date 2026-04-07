using Microsoft.JSInterop;

namespace PropaneDriver.Client.Services
{
    public class GeolocationService : IAsyncDisposable
    {
        private readonly IJSRuntime _js;
        private DotNetObjectReference<GeolocationService>? _dotNetRef;
        private bool _isWatching;

        public event Action<double, double, double>? OnPositionChanged;
        public event Action<string>? OnError;

        public double CurrentLatitude { get; private set; }
        public double CurrentLongitude { get; private set; }
        public double CurrentAccuracy { get; private set; }
        public bool IsWatching => _isWatching;

        public GeolocationService(IJSRuntime js)
        {
            _js = js;
        }

        public async Task StartWatchingAsync()
        {
            if (_isWatching) return;

            _dotNetRef = DotNetObjectReference.Create(this);
            await _js.InvokeVoidAsync("startWatchingPosition", _dotNetRef);
            _isWatching = true;
        }

        public async Task StopWatchingAsync()
        {
            if (!_isWatching) return;

            await _js.InvokeVoidAsync("stopWatchingPosition");
            _isWatching = false;
        }

        [JSInvokable]
        public void OnPositionUpdate(double latitude, double longitude, double accuracy)
        {
            CurrentLatitude = latitude;
            CurrentLongitude = longitude;
            CurrentAccuracy = accuracy;
            OnPositionChanged?.Invoke(latitude, longitude, accuracy);
        }

        [JSInvokable]
        public void OnPositionError(string message)
        {
            OnError?.Invoke(message);
        }

        public async ValueTask DisposeAsync()
        {
            await StopWatchingAsync();
            _dotNetRef?.Dispose();
        }
    }
}
