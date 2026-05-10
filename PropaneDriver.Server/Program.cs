using System.Text;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PropaneDriver.Server.Data;
using PropaneDriver.Server.Endpoints;
using PropaneDriver.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=tcp:sql-database-server-data-drive.database.windows.net,1433;Database=sql-db-data-drive;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

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
builder.Services.AddHttpClient();

// JWT bearer auth. The signing key, issuer, and audience all come from the
// "Jwt" config block. Endpoints opt into auth via .RequireAuthorization(...);
// nothing is implicitly protected, so unsecured endpoints (geocoding, login,
// password reset) keep working.
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured. Set it via appsettings or User Secrets.");
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

app.MapFallbackToFile("index.html");

app.Run();

// Marker partial declaration so PropaneDriver.Tests can target the
// implicit Program class via WebApplicationFactory<Program>.
public partial class Program { }
