using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PropaneDriver.Server.Data;
using PropaneDriver.Server.Services;

namespace PropaneDriver.Tests.Integration;

// In-process WebApplicationFactory for the real Program. Swaps the Azure
// SQL DbContext for InMemory, drops in deterministic Jwt config, and
// disables admin seeding so each fixture starts with an empty Drivers
// table. The DocumentIntelligence/Email services are AddSingleton and
// constructed lazily, so they aren't built unless a test hits an endpoint
// that needs them — auth-gated calls reject before the handler runs.
public class PropaneDriverWebAppFactory : WebApplicationFactory<Program>
{
    // Each factory instance gets its own InMemory database so tests in
    // different fixtures can't see each other's drivers.
    public string DatabaseName { get; } = $"PropaneDriverTestDb_{Guid.NewGuid()}";

    public const string JwtKey = "integration-test-signing-key-min-32-chars-long-abcdef";
    public const string JwtIssuer = "PropaneDriverTest";
    public const string JwtAudience = "PropaneDriverTestClient";

    // The connection string is never dialed — ConfigureServices replaces the
    // whole SQL DbContext registration with InMemory. An unreachable host keeps
    // it that way: if that swap ever regresses, the test fails instead of
    // reaching a real server.
    private const string UnreachableConnectionString =
        "Server=tcp:do-not-connect.invalid,1433;Database=PropaneDriverTest;";

    // ConnectionStrings:DefaultConnection and Jwt:Key are both read inline by
    // Program.cs during startup and both now throw when missing, so neither can
    // come from ConfigureAppConfiguration below — those callbacks are layered on
    // only after Program has already run. Environment variables are part of the
    // default configuration that CreateBuilder assembles, so they are visible in
    // time. This is the same channel App Service uses, "__" separator included.
    //
    // A static constructor runs before the first factory instance exists, which
    // is well before any host is built.
    static PropaneDriverWebAppFactory()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection", UnreachableConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Restated here so anything resolving config after startup —
                // JwtTokenService reads the "Jwt" section lazily at issuance
                // time — sees the test values even when a developer's
                // local.settings.json defines its own. That file is layered
                // last within Program.cs, ahead of the environment variables
                // set in the static constructor, but behind this collection.
                ["ConnectionStrings:DefaultConnection"] = UnreachableConnectionString,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:Key"] = JwtKey,
                ["Jwt:ExpirationHours"] = "1",
                // Empty password short-circuits AdminAccountSeeder so each
                // test class starts with a clean Drivers table.
                ["AdminSeed:Password"] = "",
                // Required by EmailService/DocumentIntelligenceService when
                // (and only when) those services are constructed; tests that
                // need them can resolve from DI via fixture helpers.
                ["AcsEndpoint"] = "https://acs.test.local/",
                ["AcsSenderAddress"] = "noreply@test.local",
                ["DocumentIntelligence:Endpoint"] = "https://docintel.test.local/",
                ["DocumentIntelligence:ApiKey"] = "test-api-key",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap in InMemory for the relational DbContext registration.
            // Three ServiceTypes have to go, not two: AddDbContext registers
            // DbContextOptions<T> and DbContextOptions, and as of EF Core 10
            // it also registers the options-building delegate itself as an
            // IDbContextOptionsConfiguration<T>. Leaving that third one in
            // place means Program.cs's UseSqlServer callback still runs
            // alongside ours, and its DefaultAzureCredential token fetch
            // fails on any machine without a managed identity.
            var registrationsToRemove = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(DbContextOptions<PropaneDriverDbContext>)
                    || descriptor.ServiceType == typeof(DbContextOptions)
                    || descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<PropaneDriverDbContext>))
                .ToList();

            foreach (var registration in registrationsToRemove)
            {
                services.Remove(registration);
            }

            services.AddDbContext<PropaneDriverDbContext>(options =>
                options.UseInMemoryDatabase(DatabaseName));

            // Program.cs binds Jwt:Key/Issuer/Audience onto JwtBearerOptions
            // during startup, reading whatever configuration exists at that
            // point — the environment variables from the static constructor,
            // unless a developer's local.settings.json overrides them, since
            // Program.cs layers that file after the environment. JwtTokenService
            // reads the section lazily at issuance time and so sees the in-memory
            // collection above, which wins over both. Re-bind validation here so
            // the two sides agree either way.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });
        });
    }

    // Insert a driver row directly through the same InMemory context the
    // app sees, so tokens issued for it satisfy real DB lookups too.
    public DriverDbRecord SeedDriver(string userName, string role, string password = "test-password")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        var driver = new DriverDbRecord
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            FirstName = "Test",
            MiddleName = string.Empty,
            LastName = userName,
            Email = $"{userName}@test.local",
            PhoneNumber = "555-0100",
            CreatedAt = DateTime.UtcNow,
        };

        db.Drivers.Add(driver);
        db.SaveChanges();
        return driver;
    }

    // Seed a route owned by the given driver. Used by ownership-enforcement
    // tests so we can assert "driver A cannot act on driver B's route".
    public RouteDbRecord SeedRoute(Guid driverId, DateOnly? date = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        var route = new RouteDbRecord
        {
            Id = Guid.NewGuid(),
            DriverId = driverId,
            Date = date ?? DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow,
        };
        db.Routes.Add(route);
        db.SaveChanges();
        return route;
    }

    // Seed a delivery (and a backing address) on the given route. Address
    // fields are unique-per-call so the unique-key constraint on Addresses
    // doesn't clash across tests.
    public DeliveryDbRecord SeedDelivery(Guid routeId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        var address = new AddressDbRecord
        {
            Id = Guid.NewGuid(),
            Street = $"{Random.Shared.Next(100, 9999)} Test St",
            City = "Testville",
            State = "MN",
            ZipCode = "55001",
            Latitude = 44.0,
            Longitude = -93.0,
            AvgDeliveryTimeMinutes = 0,
        };
        db.Addresses.Add(address);

        var delivery = new DeliveryDbRecord
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            AddressId = address.Id,
            CustomerName = "Test Customer",
            Status = 0,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
        };
        db.Deliveries.Add(delivery);
        db.SaveChanges();
        return delivery;
    }

    public AlertDbRecord SeedAlert(Guid deliveryId, string message = "test alert")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();

        var alert = new AlertDbRecord
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            Message = message,
            CreatedAt = DateTime.UtcNow,
        };
        db.Alerts.Add(alert);
        db.SaveChanges();
        return alert;
    }

    // Issues a JWT signed with the same key the test app validates against.
    public string IssueToken(DriverDbRecord driver)
    {
        using var scope = Services.CreateScope();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        return jwtTokenService.CreateTokenForDriver(driver);
    }

    // CreateClient default is http://localhost, which the
    // UseHttpsRedirection middleware bounces with a 307. Forcing https://
    // makes the request look pre-redirected so the middleware no-ops.
    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    public HttpClient CreateClientForDriver(DriverDbRecord driver)
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueToken(driver));
        return client;
    }
}
