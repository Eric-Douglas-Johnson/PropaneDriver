using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PropaneDriver.Client;
using PropaneDriver.Client.Authentication;
using PropaneDriver.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

builder.Services.AddScoped<BrowserStorageService>();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(provider =>
    provider.GetRequiredService<CustomAuthStateProvider>());

builder.Services.AddScoped<ErrorLogService>();
builder.Services.AddScoped<DeliveryTimeApiService>();
builder.Services.AddScoped<GeolocationService>();
builder.Services.AddScoped<GeoFenceService>();

await builder.Build().RunAsync();
