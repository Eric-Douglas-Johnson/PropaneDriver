using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddHttpClient();

var app = builder.Build();

// Bootstrap the database schema (idempotent raw SQL; we don't use EF migrations).
DatabaseInitializer.EnsureCreated(app.Services, app.Logger);

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

app.MapFallbackToFile("index.html");

app.Run();
