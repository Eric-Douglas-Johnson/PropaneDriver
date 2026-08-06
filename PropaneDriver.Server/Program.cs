using System.Text;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PropaneDriver.Server.Data;
using PropaneDriver.Server.Endpoints;
using PropaneDriver.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Local-only configuration overlay, and the only source of configuration for a
// local run — appsettings.json is empty, so there is nothing underneath this.
// Gitignored, excluded from the Docker build context, and removed from Content
// so it can never be published; the file simply does not exist in the container,
// which is what keeps dev values and production values from ever crossing.
// Added last, so it also takes precedence over user secrets when both define a
// key. In App Service every one of these keys arrives as an application
// setting instead.
builder.Configuration.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

// No hardcoded fallback on purpose. The old default pointed at the production
// Azure SQL server, so a machine with no connection string configured would
// silently read and write production data. Failing here instead makes the
// misconfiguration obvious. App Service supplies this as the
// ConnectionStrings__DefaultConnection application setting; locally it comes
// from local.settings.json.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Copy "
        + "local.settings.example.json to local.settings.json and set it.");

builder.Services.AddDbContext<PropaneDriverDbContext>(options =>
{
    var sqlConnection = new SqlConnection(connectionString);
    var credential = new DefaultAzureCredential();
    var token = credential.GetToken(new Azure.Core.TokenRequestContext(
        new[] { "https://database.windows.net/.default" }));
    sqlConnection.AccessToken = token.Token;
    options.UseSqlServer(sqlConnection, sql => sql.EnableRetryOnFailure(
        maxRetryCount: 6,
        maxRetryDelay: TimeSpan.FromSeconds(15),
        errorNumbersToAdd: null));
});

builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var endpoint = configuration["DocumentIntelligence:Endpoint"];
    if (string.IsNullOrWhiteSpace(endpoint))
        throw new InvalidOperationException("DocumentIntelligence:Endpoint not configured.");

    var apiKey = configuration["DocumentIntelligence:ApiKey"];
    return string.IsNullOrWhiteSpace(apiKey)
        ? new Azure.AI.DocumentIntelligence.DocumentIntelligenceClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential())
        : new Azure.AI.DocumentIntelligence.DocumentIntelligenceClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
});
builder.Services.AddSingleton<DocumentIntelligenceService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<EiaFuelPriceService>();
builder.Services.AddHttpClient();

// JWT bearer auth. The signing key, issuer, and audience all come from the
// "Jwt" config block. App Service supplies the key as the Jwt__Key application
// setting, and local runs get it from local.settings.json — nothing is
// committed. Endpoints opt into auth via
// .RequireAuthorization(...); nothing is implicitly protected, so unsecured
// endpoints (geocoding, login, password reset) keep working.
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is not configured. Copy local.settings.example.json to "
        + "local.settings.json and set it, or use User Secrets.");
var jwtIssuer = jwtSection["Issuer"] ?? "PropaneDriver";
var jwtAudience = jwtSection["Audience"] ?? "PropaneDriverClient";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("AuthenticatedDriver", policy => policy.RequireAuthenticatedUser());
});

// OpenTelemetry, exported to Application Insights. Only wired up when a
// connection string is actually configured — UseAzureMonitor throws if it
// can't find one, which would take down local runs and every integration
// test that boots the real Program via WebApplicationFactory. In App Service
// the setting arrives as an environment variable of the same name; locally it
// comes from local.settings.json or user secrets, and is normally absent.
var applicationInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(azureMonitorOptions =>
        azureMonitorOptions.ConnectionString = applicationInsightsConnectionString);
}

var app = builder.Build();

// Bootstrap the database schema (idempotent raw SQL; we don't use EF migrations).
DatabaseInitializer.EnsureCreated(app.Services, app.Logger);

// Seed/refresh the admin account from "AdminSeed" config. Runs every startup
// but is idempotent — won't overwrite an existing admin's password.
AdminAccountSeeder.EnsureAdminSeeded(app.Services, app.Logger);

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// Minimal-API endpoint modules. Each file under Endpoints/ owns one resource's
// routes via an IEndpointRouteBuilder extension method.
app.MapDriverEndpoints();
app.MapRouteEndpoints();
app.MapAuthEndpoints();
app.MapDeliveryEndpoints();
app.MapAddressEndpoints();
app.MapAlertEndpoints();
app.MapDeliveryTimeEndpoints();
app.MapGeocodingEndpoints();
app.MapConfigEndpoints();
app.MapClientLogEndpoints();
app.MapImportEndpoints();
app.MapFuelLogEndpoints();
app.MapFuelPriceEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

// Marker partial declaration so PropaneDriver.Tests can target the
// implicit Program class via WebApplicationFactory<Program>.
public partial class Program { }
