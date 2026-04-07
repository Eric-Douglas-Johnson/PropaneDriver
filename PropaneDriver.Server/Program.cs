using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

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
    options.UseSqlServer(sqlConnection);
});

var app = builder.Build();

// Ensure the database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
    db.Database.EnsureCreated();
}

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

// Stub auth endpoint - TODO: implement real authentication
app.MapPost("api/Authenticate", (CredsDto creds) =>
{
    return Results.Ok(new AuthResponseDto
    {
        IsAuthenticated = true,
        Role = 1,
        UserId = Guid.NewGuid(),
        StatusMessage = "Stub - implement real auth"
    });
});

// Stub driver endpoint - TODO: implement real driver lookup
app.MapGet("driver/{id:guid}", (Guid id) =>
{
    return Results.Ok(new DriverDto
    {
        Id = id.ToString(),
        UserName = "stub_driver",
        Role = "driver",
        FirstName = "Stub",
        LastName = "Driver"
    });
});

// Stub registration endpoint - TODO: implement real registration
app.MapPost("api/Register", (RegisterDriverDto registration) =>
{
    return Results.Ok(new { Message = "Stub - registration accepted" });
});

// Store a delivery time record
app.MapPost("api/delivery-times", async (DeliveryTimeDto dto, PropaneDriverDbContext db) =>
{
    var entity = new DeliveryTimeEntity
    {
        DeliveryId = dto.DeliveryId,
        Address = dto.Address,
        Latitude = dto.Latitude,
        Longitude = dto.Longitude,
        TimeIntervalSeconds = dto.TimeIntervalSeconds,
        RecordedAt = DateTime.UtcNow
    };

    db.DeliveryTimes.Add(entity);
    await db.SaveChangesAsync();

    return Results.Ok(new { entity.Id, entity.RecordedAt });
});

// Get average delivery time for an address
app.MapGet("api/delivery-times/average/{address}", async (string address, PropaneDriverDbContext db) =>
{
    var decodedAddress = Uri.UnescapeDataString(address);

    var times = await db.DeliveryTimes
        .Where(t => t.Address == decodedAddress)
        .Select(t => t.TimeIntervalSeconds)
        .ToListAsync();

    if (times.Count == 0)
        return Results.Ok(new { Address = decodedAddress, AverageSeconds = 0.0, Count = 0 });

    var avg = times.Average();
    return Results.Ok(new { Address = decodedAddress, AverageSeconds = avg, Count = times.Count });
});

app.MapFallbackToFile("index.html");

app.Run();
