using Microsoft.JSInterop;

namespace PropaneDriver.Client.Services;

public class SpeechService(IJSRuntime js)
{
    private sealed record SpeechResult(
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] string Error);

    public async Task<string?> DictateAsync()
    {
        var (text, error) = await DictateDetailedAsync();
        return string.IsNullOrEmpty(error) ? text : null;
    }

    public async Task<(string? Text, string? Error)> DictateDetailedAsync()
    {
        var result = await js.InvokeAsync<SpeechResult>("startDictation");
        return (
            string.IsNullOrEmpty(result.Text) ? null : result.Text,
            string.IsNullOrEmpty(result.Error) ? null : result.Error);
    }
}
