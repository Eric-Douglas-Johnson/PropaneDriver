using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PropaneDriver.Client;
using PropaneDriver.Client.Authentication;
using PropaneDriver.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using PropaneDriver.Client.ApiConsumers;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<BrowserStorageService>();
builder.Services.AddScoped<BearerTokenHandler>();

// Wrap the HttpClient in a BearerTokenHandler so every outbound request gets
// the persisted JWT (when present) — the server now requires it on every
// admin-gated endpoint.
builder.Services.AddScoped(sp =>
{
    var bearerHandler = sp.GetRequiredService<BearerTokenHandler>();
    bearerHandler.InnerHandler = new HttpClientHandler();
    return new HttpClient(bearerHandler)
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
});

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(provider =>
    provider.GetRequiredService<CustomAuthStateProvider>());

builder.Services.AddScoped<DeliveryTimeApiConsumer>();
builder.Services.AddScoped<RouteApiConsumer>();
builder.Services.AddScoped<DeliveryApiConsumer>();
builder.Services.AddScoped<AddressApiConsumer>();
builder.Services.AddScoped<FuelLogApiConsumer>();
builder.Services.AddScoped<GeolocationService>();
builder.Services.AddScoped<GeocodingService>();
builder.Services.AddScoped<DeliveryTimerService>();
builder.Services.AddScoped<DeliveryCompletionService>();
builder.Services.AddScoped<GeoFenceService>();
builder.Services.AddScoped<SpeechService>();

// Point the static client-side error logger at the app's own origin so its
// relative "api/client-logs" posts resolve instead of throwing.
ErrorLogApiConsumer.Initialize(builder.HostEnvironment.BaseAddress);

await builder.Build().RunAsync();
