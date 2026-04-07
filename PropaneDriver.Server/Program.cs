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

    // Create the Drivers table if it doesn't exist (EnsureCreated skips existing DBs)
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Drivers')
        BEGIN
            CREATE TABLE [Drivers] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                [UserName] nvarchar(100) NOT NULL,
                [PasswordHash] nvarchar(max) NOT NULL,
                [Role] nvarchar(50) NOT NULL,
                [FirstName] nvarchar(100) NOT NULL,
                [MiddleName] nvarchar(100) NOT NULL,
                [LastName] nvarchar(100) NOT NULL,
                [Email] nvarchar(255) NOT NULL,
                [PhoneNumber] nvarchar(30) NOT NULL,
                [LicenseClass] nvarchar(50) NOT NULL,
                [TruckNumber] nvarchar(50) NOT NULL,
                [CreatedAt] datetime2 NOT NULL
            );
            CREATE UNIQUE INDEX [IX_Drivers_UserName] ON [Drivers] ([UserName]);
            CREATE INDEX [IX_Drivers_Email] ON [Drivers] ([Email]);
        END
    ");
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

// Register a new driver
app.MapPost("api/Register", async (RegisterDriverDto registration, PropaneDriverDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(registration.UserName) || string.IsNullOrWhiteSpace(registration.Password))
        return Results.BadRequest(new { Message = "UserName and Password are required." });

    var exists = await db.Drivers.AnyAsync(d => d.UserName == registration.UserName);
    if (exists)
        return Results.Conflict(new { Message = "A driver with that user name already exists." });

    var driver = new DriverEntity
    {
        Id = Guid.NewGuid(),
        UserName = registration.UserName,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(registration.Password),
        Role = "driver",
        FirstName = registration.FirstName,
        MiddleName = registration.MiddleName,
        LastName = registration.LastName,
        Email = registration.Email,
        PhoneNumber = registration.PhoneNumber,
        CreatedAt = DateTime.UtcNow
    };

    db.Drivers.Add(driver);
    await db.SaveChangesAsync();

    return Results.Ok(new { driver.Id, Message = "Driver registered successfully." });
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
