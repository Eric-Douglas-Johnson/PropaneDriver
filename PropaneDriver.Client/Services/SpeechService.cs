using Microsoft.JSInterop;

namespace PropaneDriver.Client.Services;

public class SpeechService(IJSRuntime js)
{
    public async Task<string?> DictateAsync()
    {
        var (text, error) = await DictateDetailedAsync();
        return string.IsNullOrEmpty(error) ? text : null;
    }

    // Same capture, but hands back the recognizer's error reason instead of
    // swallowing it — callers that want to tell the driver *why* nothing was
    // heard (unsupported browser, mic denied, no speech) use this overload.
    public async Task<(string? Text, string? Error)> DictateDetailedAsync()
    {
        var result = await js.InvokeAsync<SpeechResult>("startDictation");
        return (
            string.IsNullOrEmpty(result.Text) ? null : result.Text,
            string.IsNullOrEmpty(result.Error) ? null : result.Error);
    }

    private sealed record SpeechResult(
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] string Error);
}
