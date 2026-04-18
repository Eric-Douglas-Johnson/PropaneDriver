using System.Security.Cryptography;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Dtos;
using System.Net.Http.Json;

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

// Ensure ErrorLog table exists
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ErrorLog')
            BEGIN
                CREATE TABLE [ErrorLog] (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [Source] nvarchar(200) NOT NULL,
                    [Level] nvarchar(50) NOT NULL,
                    [Message] nvarchar(max) NOT NULL,
                    [Timestamp] datetime2 NOT NULL
                );
                CREATE INDEX [IX_ErrorLog_Source] ON [ErrorLog] ([Source]);
                CREATE INDEX [IX_ErrorLog_Timestamp] ON [ErrorLog] ([Timestamp]);
            END

            IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DeliveryStatus')
            BEGIN
                DROP TABLE [DeliveryStatus];
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Routes')
            BEGIN
                CREATE TABLE [Routes] (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [DriverId] uniqueidentifier NOT NULL,
                    [Date] date NOT NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
                CREATE INDEX [IX_Routes_DriverId] ON [Routes] ([DriverId]);
                CREATE INDEX [IX_Routes_Date] ON [Routes] ([Date]);
                CREATE INDEX [IX_Routes_DriverId_Date] ON [Routes] ([DriverId], [Date]);
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Deliveries')
            BEGIN
                CREATE TABLE [Deliveries] (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [RouteId] uniqueidentifier NOT NULL,
                    [CustomerName] nvarchar(200) NOT NULL,
                    [Street] nvarchar(200) NOT NULL,
                    [City] nvarchar(100) NOT NULL,
                    [State] nvarchar(50) NOT NULL,
                    [ZipCode] nvarchar(20) NOT NULL,
                    [Latitude] float NOT NULL,
                    [Longitude] float NOT NULL,
                    [Status] int NOT NULL,
                    [AvgDeliveryTimeMinutes] float NOT NULL,
                    [SortOrder] int NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_Deliveries_Routes_RouteId] FOREIGN KEY ([RouteId])
                        REFERENCES [Routes] ([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_Deliveries_RouteId] ON [Deliveries] ([RouteId]);
                CREATE INDEX [IX_Deliveries_RouteId_SortOrder] ON [Deliveries] ([RouteId], [SortOrder]);
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Alerts')
            BEGIN
                CREATE TABLE [Alerts] (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [DeliveryId] uniqueidentifier NOT NULL,
                    [Message] nvarchar(500) NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_Alerts_Deliveries_DeliveryId] FOREIGN KEY ([DeliveryId])
                        REFERENCES [Deliveries] ([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_Alerts_DeliveryId] ON [Alerts] ([DeliveryId]);
            END
        ");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Startup table creation failed; will retry on first request.");
    }
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

// List all drivers (for admin route-builder)
app.MapGet("api/drivers", async (PropaneDriverDbContext db) =>
{
    var drivers = await db.Drivers
        .AsNoTracking()
        .OrderBy(d => d.LastName).ThenBy(d => d.FirstName)
        .Select(d => new DriverDto
        {
            Id = d.Id.ToString(),
            UserName = d.UserName,
            FirstName = d.FirstName,
            MiddleName = d.MiddleName,
            LastName = d.LastName,
            Email = d.Email,
            PhoneNumber = d.PhoneNumber,
            Role = d.Role
        })
        .ToListAsync();
    return Results.Ok(drivers);
});

// Get a route (with deliveries) for a driver on a specific date
app.MapGet("api/routes/{driverId:guid}/{date}", async (Guid driverId, DateOnly date, PropaneDriverDbContext db) =>
{
    var route = await db.Routes
        .AsNoTracking()
        .Where(r => r.DriverId == driverId && r.Date == date)
        .Select(r => new RouteDto
        {
            Id = r.Id.ToString(),
            DriverId = r.DriverId.ToString(),
            Date = r.Date,
            Deliveries = r.Deliveries
                .OrderBy(d => d.SortOrder)
                .Select(d => new DeliveryDto
                {
                    Id = d.Id.ToString(),
                    CustomerName = d.CustomerName,
                    Date = r.Date,
                    Location = new GeoAddressDto
                    {
                        Street = d.Street,
                        City = d.City,
                        State = d.State,
                        ZipCode = d.ZipCode,
                        Latitude = d.Latitude,
                        Longitude = d.Longitude
                    },
                    AvgDeliveryTimeMinutes = d.AvgDeliveryTimeMinutes,
                    Status = d.Status,
                    Alerts = d.Alerts
                        .OrderBy(a => a.CreatedAt)
                        .Select(a => new AlertDto
                        {
                            Id = a.Id.ToString(),
                            DeliveryId = a.DeliveryId.ToString(),
                            Message = a.Message,
                            CreatedAt = a.CreatedAt
                        })
                        .ToList()
                })
                .ToList()
        })
        .FirstOrDefaultAsync();

    return route is null ? Results.NotFound() : Results.Ok(route);
});

// List all routes for a driver (summary info)
app.MapGet("api/routes/driver/{driverId:guid}", async (Guid driverId, PropaneDriverDbContext db) =>
{
    var routes = await db.Routes
        .AsNoTracking()
        .Where(r => r.DriverId == driverId)
        .OrderByDescending(r => r.Date)
        .Select(r => new RouteListItemDto
        {
            Id = r.Id.ToString(),
            Date = r.Date,
            DeliveryCount = r.Deliveries.Count(),
            CompletedCount = r.Deliveries.Count(d => d.Status == 2)
        })
        .ToListAsync();

    return Results.Ok(routes);
});

// Delete a route and its deliveries
app.MapDelete("api/routes/{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
{
    var route = await db.Routes.FindAsync(id);
    if (route is null) return Results.NotFound();

    db.Routes.Remove(route); // cascade deletes deliveries
    await db.SaveChangesAsync();
    return Results.Ok(new { Deleted = true, RouteId = id });
});

// Authenticate a driver
app.MapPost("api/Authenticate", async (CredsDto creds, PropaneDriverDbContext db) =>
{
    var driver = await db.Drivers.FirstOrDefaultAsync(d => d.UserName == creds.UserName);

    if (driver is null)
    {
        return Results.Ok(new AuthResponseDto
        {
            IsAuthenticated = false,
            Role = 0,
            UserId = Guid.Empty,
            StatusMessage = $"No driver found with user name '{creds.UserName}'."
        });
    }

    if (!BCrypt.Net.BCrypt.Verify(creds.Password, driver.PasswordHash))
    {
        return Results.Ok(new AuthResponseDto
        {
            IsAuthenticated = false,
            Role = 0,
            UserId = Guid.Empty,
            StatusMessage = "Invalid password."
        });
    }

    return Results.Ok(new AuthResponseDto
    {
        IsAuthenticated = true,
        Role = 1,
        UserId = driver.Id,
        StatusMessage = "Authenticated"
    });
});

// Get driver by ID
app.MapGet("driver/{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
{
    var driver = await db.Drivers.FindAsync(id);

    if (driver is null)
        return Results.NotFound();

    return Results.Ok(new DriverDto
    {
        Id = driver.Id.ToString(),
        UserName = driver.UserName,
        Role = driver.Role,
        FirstName = driver.FirstName,
        MiddleName = driver.MiddleName,
        LastName = driver.LastName,
        Email = driver.Email,
        PhoneNumber = driver.PhoneNumber
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

// Request a password reset email
app.MapPost("api/ForgotPassword", async (ForgotPasswordDto dto, PropaneDriverDbContext db, EmailService emailService, IConfiguration config) =>
{
    // Always return success to avoid leaking which emails exist
    var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Email == dto.Email);
    if (driver is null)
        return Results.Ok(new { Message = "If that email is registered, a reset link has been sent." });

    // Generate a cryptographically random token
    var tokenBytes = RandomNumberGenerator.GetBytes(32);
    var rawToken = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    var tokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

    // Invalidate any unused tokens for this driver
    var existing = await db.PasswordResetTokens
        .Where(t => t.DriverId == driver.Id && t.UsedAt == null)
        .ToListAsync();
    foreach (var t in existing)
        t.UsedAt = DateTime.UtcNow;

    db.PasswordResetTokens.Add(new PasswordResetTokenEntity
    {
        DriverId = driver.Id,
        TokenHash = tokenHash,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddHours(1)
    });
    await db.SaveChangesAsync();

    var baseUrl = config["AppBaseUrl"] ?? "https://propane-driver.azurewebsites.net";
    var resetUrl = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

    await emailService.SendPasswordResetAsync(driver.Email, $"{driver.FirstName} {driver.LastName}".Trim(), resetUrl);

    return Results.Ok(new { Message = "If that email is registered, a reset link has been sent." });
});

// Reset the password using a token
app.MapPost("api/ResetPassword", async (ResetPasswordDto dto, PropaneDriverDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
        return Results.BadRequest(new { Message = "Token and new password are required." });

    if (dto.NewPassword.Length < 6)
        return Results.BadRequest(new { Message = "Password must be at least 6 characters." });

    var tokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dto.Token)));

    var resetToken = await db.PasswordResetTokens
        .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

    if (resetToken is null || resetToken.UsedAt != null || resetToken.ExpiresAt < DateTime.UtcNow)
        return Results.BadRequest(new { Message = "This reset link is invalid or has expired." });

    var driver = await db.Drivers.FindAsync(resetToken.DriverId);
    if (driver is null)
        return Results.BadRequest(new { Message = "Driver not found." });

    driver.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
    resetToken.UsedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    return Results.Ok(new { Message = "Password has been reset successfully." });
});

// Store a delivery time record
app.MapPost("api/delivery-times", async (DeliveryTimeDto dto, PropaneDriverDbContext db) =>
{
    try
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

        app.Logger.LogInformation("Saved delivery time Id={Id}", entity.Id);
        return Results.Ok(new { entity.Id, entity.RecordedAt });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to save delivery time for {Address}", dto.Address);
        return Results.Problem(
            detail: ex.Message,
            title: "Failed to save delivery time",
            statusCode: 500);
    }
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

    times.Sort();

    //Once we have at least 5 data points, remove the shortest and longest times
    //in order to remove outliers
    if (times.Count > 4)
    {
        times.RemoveAt(times.Count - 1);
        times.RemoveAt(0);
    }

    var avg = times.Average();
    return Results.Ok(new { Address = decodedAddress, AverageSeconds = avg, Count = times.Count });
});

// Get today's route (with deliveries) for a driver
app.MapGet("api/routes/today/{driverId:guid}", async (Guid driverId, PropaneDriverDbContext db) =>
{
    var today = DateOnly.FromDateTime(DateTime.Today);
    var route = await db.Routes
        .AsNoTracking()
        .Where(r => r.DriverId == driverId && r.Date == today)
        .Select(r => new RouteDto
        {
            Id = r.Id.ToString(),
            DriverId = r.DriverId.ToString(),
            Date = r.Date,
            Deliveries = r.Deliveries
                .OrderBy(d => d.SortOrder)
                .Select(d => new DeliveryDto
                {
                    Id = d.Id.ToString(),
                    CustomerName = d.CustomerName,
                    Date = r.Date,
                    Location = new GeoAddressDto
                    {
                        Street = d.Street,
                        City = d.City,
                        State = d.State,
                        ZipCode = d.ZipCode,
                        Latitude = d.Latitude,
                        Longitude = d.Longitude
                    },
                    AvgDeliveryTimeMinutes = d.AvgDeliveryTimeMinutes,
                    Status = d.Status,
                    Alerts = d.Alerts
                        .OrderBy(a => a.CreatedAt)
                        .Select(a => new AlertDto
                        {
                            Id = a.Id.ToString(),
                            DeliveryId = a.DeliveryId.ToString(),
                            Message = a.Message,
                            CreatedAt = a.CreatedAt
                        })
                        .ToList()
                })
                .ToList()
        })
        .FirstOrDefaultAsync();

    return route is null ? Results.NotFound() : Results.Ok(route);
});

// Create a route with deliveries
app.MapPost("api/routes", async (CreateRouteDto dto, PropaneDriverDbContext db) =>
{
    if (!Guid.TryParse(dto.DriverId, out var driverId))
        return Results.BadRequest(new { Message = "DriverId must be a valid GUID." });

    try
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            DriverId = driverId,
            Date = dto.Date,
            CreatedAt = DateTime.UtcNow,
            Deliveries = dto.Deliveries.Select((d, i) => new DeliveryEntity
            {
                Id = Guid.NewGuid(),
                CustomerName = d.CustomerName,
                Street = d.Street,
                City = d.City,
                State = d.State,
                ZipCode = d.ZipCode,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                AvgDeliveryTimeMinutes = d.AvgDeliveryTimeMinutes,
                SortOrder = d.SortOrder == 0 ? i : d.SortOrder,
                Status = 0,
                CreatedAt = DateTime.UtcNow
            }).ToList()
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync();

        return Results.Ok(new { route.Id, DeliveryCount = route.Deliveries.Count });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to create route for driver {DriverId}", dto.DriverId);
        return Results.Problem(detail: ex.Message, title: "Failed to create route", statusCode: 500);
    }
});

// List alerts for a delivery
app.MapGet("api/deliveries/{id:guid}/alerts", async (Guid id, PropaneDriverDbContext db) =>
{
    var deliveryExists = await db.Deliveries.AnyAsync(d => d.Id == id);
    if (!deliveryExists) return Results.NotFound();

    var alerts = await db.Alerts
        .AsNoTracking()
        .Where(a => a.DeliveryId == id)
        .OrderBy(a => a.CreatedAt)
        .Select(a => new AlertDto
        {
            Id = a.Id.ToString(),
            DeliveryId = a.DeliveryId.ToString(),
            Message = a.Message,
            CreatedAt = a.CreatedAt
        })
        .ToListAsync();

    return Results.Ok(alerts);
});

// Create an alert for a delivery
app.MapPost("api/deliveries/{id:guid}/alerts", async (Guid id, CreateAlertDto dto, PropaneDriverDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Message))
        return Results.BadRequest(new { Message = "Alert message is required." });

    var deliveryExists = await db.Deliveries.AnyAsync(d => d.Id == id);
    if (!deliveryExists) return Results.NotFound();

    var alert = new AlertEntity
    {
        Id = Guid.NewGuid(),
        DeliveryId = id,
        Message = dto.Message.Trim(),
        CreatedAt = DateTime.UtcNow
    };

    db.Alerts.Add(alert);
    await db.SaveChangesAsync();

    return Results.Ok(new AlertDto
    {
        Id = alert.Id.ToString(),
        DeliveryId = alert.DeliveryId.ToString(),
        Message = alert.Message,
        CreatedAt = alert.CreatedAt
    });
});

// Delete an alert
app.MapDelete("api/alerts/{id:guid}", async (Guid id, PropaneDriverDbContext db) =>
{
    var alert = await db.Alerts.FindAsync(id);
    if (alert is null) return Results.NotFound();

    db.Alerts.Remove(alert);
    await db.SaveChangesAsync();
    return Results.Ok(new { Deleted = true, AlertId = id });
});

// Update a delivery's status
app.MapPut("api/deliveries/{id:guid}/status", async (Guid id, DeliveryStatusUpdateDto dto, PropaneDriverDbContext db) =>
{
    try
    {
        var delivery = await db.Deliveries.FindAsync(id);
        if (delivery is null)
            return Results.NotFound();

        delivery.Status = dto.Status;
        await db.SaveChangesAsync();
        return Results.Ok(new { delivery.Id, delivery.Status });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to update status for delivery {Id}", id);
        return Results.Problem(detail: ex.Message, title: "Failed to update delivery status", statusCode: 500);
    }
});

// Return the browser-facing Google Maps JS API key. This key is HTTP-referrer
// restricted in the Google Cloud Console, so exposing it here is acceptable.
// Separate from the server-side Geocoding key, which never leaves the server.
app.MapGet("api/config/maps-key", (IConfiguration config) =>
{
    var key = config["GoogleMaps:JsApiKey"]
        ?? Environment.GetEnvironmentVariable("GOOGLE_MAPS_JS_API_KEY");

    if (string.IsNullOrWhiteSpace(key))
    {
        app.Logger.LogError("Google Maps JS API key is not configured.");
        return Results.Problem(
            detail: "Maps key is not configured.",
            statusCode: 500);
    }

    return Results.Ok(new { key });
});

// Geocode an address via Google (proxied so the API key stays server-side).
// Tries the Geocoding API first (structured, cheap). On ZERO_RESULTS falls
// back to Places Text Search, which behaves like the maps.google.com search
// box and handles rural / colloquial addresses much better.
app.MapGet("api/geocode", async (
    string? street, string? city, string? state, string? zip,
    IHttpClientFactory httpFactory, IConfiguration config) =>
{
    var apiKey = config["GoogleGeocoding:ApiKey"]
        ?? Environment.GetEnvironmentVariable("GOOGLE_GEOCODING_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        app.Logger.LogError("Google Geocoding API key is not configured.");
        return Results.Problem(
            detail: "Geocoding is not configured.",
            statusCode: 500);
    }

    var parts = new List<string>();
    if (!string.IsNullOrWhiteSpace(street)) parts.Add(street.Trim());
    if (!string.IsNullOrWhiteSpace(city)) parts.Add(city.Trim());
    if (!string.IsNullOrWhiteSpace(state)) parts.Add(state.Trim());
    if (!string.IsNullOrWhiteSpace(zip)) parts.Add(zip.Trim());

    if (parts.Count == 0)
        return Results.BadRequest(new { Message = "No address parts provided." });

    var rawAddress = string.Join(", ", parts);
    var encodedAddress = Uri.EscapeDataString(rawAddress);
    var http = httpFactory.CreateClient();

    // --- Attempt 1: Geocoding API -----------------------------------------
    var geocodeUrl = $"https://maps.googleapis.com/maps/api/geocode/json?address={encodedAddress}&region=us&key={apiKey}";

    GoogleGeocodeResult? best = null;
    string? geocodeStatus = null;
    string? geocodeError = null;
    string? placesStatus = null;
    string? placesError = null;

    try
    {
        var resp = await http.GetFromJsonAsync<GoogleGeocodeResponse>(geocodeUrl);
        geocodeStatus = resp?.Status;
        geocodeError = resp?.ErrorMessage;

        if (resp is not null && resp.Status == "OK" && resp.Results.Length > 0)
        {
            best = resp.Results[0];
        }
        else if (resp is not null && resp.Status != "OK" && resp.Status != "ZERO_RESULTS")
        {
            app.Logger.LogWarning("Google Geocoding returned status {Status}: {Error}", resp.Status, resp.ErrorMessage);
        }
    }
    catch (Exception ex)
    {
        geocodeError = ex.Message;
        app.Logger.LogError(ex, "Google geocoding request failed");
    }

    // --- Attempt 2: Places Text Search fallback ---------------------------
    // Used when Geocoding says ZERO_RESULTS (or fails). Text Search is what
    // the Maps search box uses and handles rural / partial addresses much
    // better — at the cost of a slightly more expensive API call.
    if (best is null)
    {
        var placesUrl = $"https://maps.googleapis.com/maps/api/place/textsearch/json?query={encodedAddress}&region=us&key={apiKey}";

        try
        {
            var placesResp = await http.GetFromJsonAsync<GoogleGeocodeResponse>(placesUrl);
            placesStatus = placesResp?.Status;
            placesError = placesResp?.ErrorMessage;

            if (placesResp is not null && placesResp.Status == "OK" && placesResp.Results.Length > 0)
            {
                best = placesResp.Results[0];
            }
            else if (placesResp is not null && placesResp.Status != "OK" && placesResp.Status != "ZERO_RESULTS")
            {
                app.Logger.LogWarning(
                    "Google Places Text Search returned status {Status}: {Error}",
                    placesResp.Status, placesResp.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            placesError = ex.Message;
            app.Logger.LogError(ex, "Google Places Text Search request failed");
        }
    }

    if (best is null)
    {
        // Include Google's status codes in the response so the client can
        // surface them — makes it easy to tell whether the key is
        // misconfigured, the API isn't enabled, or the address truly
        // doesn't exist in Google's index.
        return Results.Json(
            new
            {
                Query = rawAddress,
                GeocodeStatus = geocodeStatus,
                GeocodeError = geocodeError,
                PlacesStatus = placesStatus,
                PlacesError = placesError
            },
            statusCode: 404);
    }

    return Results.Ok(new GeocodingResultDto
    {
        Latitude = best.Geometry.Location.Lat,
        Longitude = best.Geometry.Location.Lng,
        DisplayName = string.IsNullOrWhiteSpace(best.FormattedAddress) ? rawAddress : best.FormattedAddress
    });
});

// Log a client-side error
app.MapPost("api/client-logs", async (ClientLogDto log, PropaneDriverDbContext db) =>
{
    try
    {
        var entity = new ErrorLogEntity
        {
            Id = Guid.NewGuid(),
            Source = log.Source ?? "Unknown",
            Level = log.Level ?? "Error",
            Message = log.Message ?? "",
            Timestamp = log.Timestamp ?? DateTime.UtcNow
        };

        db.ErrorLogs.Add(entity);
        await db.SaveChangesAsync();

        return Results.Ok(new { entity.Id });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to persist client error log");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.MapFallbackToFile("index.html");

app.Run();
