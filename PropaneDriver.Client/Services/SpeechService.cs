using Microsoft.JSInterop;

namespace PropaneDriver.Client.Services;

public class SpeechService(IJSRuntime js)
{
    public async Task<string?> DictateAsync()
    {
        var result = await js.InvokeAsync<SpeechResult>("startDictation");
        return !string.IsNullOrEmpty(result.Text) && string.IsNullOrEmpty(result.Error)
            ? result.Text
            : null;
    }

    private sealed record SpeechResult(
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] string Error);
}
