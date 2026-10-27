using Microsoft.JSInterop;

namespace PropaneDriver.Client.Services;

public class SpeechService(IJSRuntime js)
{
    private sealed record SpeechResult(
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] string Error);

    // Room for a spoken note; callers capturing a short value can pass less.
    private static readonly TimeSpan DefaultMaxListenDuration = TimeSpan.FromSeconds(10);

    public async Task<string?> DictateAsync()
    {
        var (text, error) = await DictateDetailedAsync();
        return string.IsNullOrEmpty(error) ? text : null;
    }

    public async Task<(string? Text, string? Error)> DictateDetailedAsync(TimeSpan? maxListenDuration = null)
    {
        var maxListenMilliseconds = (int)(maxListenDuration ?? DefaultMaxListenDuration).TotalMilliseconds;

        SpeechResult? result;
        try
        {
            result = await js.InvokeAsync<SpeechResult?>("startDictation", maxListenMilliseconds);
        }
        catch (Exception ex)
        {
            // Report rather than throw: callers clear their "Listening…" state only when this returns.
            return (null, ex.Message);
        }

        if (result is null) return (null, "unknown error");
        return (
            string.IsNullOrEmpty(result.Text) ? null : result.Text,
            string.IsNullOrEmpty(result.Error) ? null : result.Error);
    }
}
