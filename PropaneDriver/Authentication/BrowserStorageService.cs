
using Microsoft.JSInterop;
using System.Text.Json;

namespace PropaneDriver.Authentication
{
    public class BrowserStorageService
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly string StorageType = "localStorage";
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions();

        public BrowserStorageService(IJSRuntime jSRuntime)
        {
            _jsRuntime = jSRuntime;
        }

        public async Task SaveToStorageAsync<T>(string key, T value)
        {
            var serializedData = Serialize(value);
            await _jsRuntime.InvokeVoidAsync($"{StorageType}.setItem", key, serializedData);
        }

        public async Task<T?> GetFromStorage<T>(string key)
        {
            var serializedData = await _jsRuntime.InvokeAsync<string?>($"{StorageType}.getItem", key);
            return Deserialize<T>(serializedData);
        }

        public async Task RemoveFromStorage(string key)
        {
            await _jsRuntime.InvokeVoidAsync($"{StorageType}.removeItem", key);
        }

        private static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _jsonSerializerOptions);
        }

        private static T? Deserialize<T>(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return JsonSerializer.Deserialize<T>(value, _jsonSerializerOptions);
            }
            else
            {
                return default;
            }
        }
    }   
}
