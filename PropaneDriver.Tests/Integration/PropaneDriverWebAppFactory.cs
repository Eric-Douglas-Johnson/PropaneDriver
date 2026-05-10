using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
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
            // Removing both ServiceType variants because AddDbContext
            // registers DbContextOptions<T> AND DbContextOptions.
            var typedOptions = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<PropaneDriverDbContext>));
            if (typedOptions is not null) services.Remove(typedOptions);

            var untypedOptions = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions));
            if (untypedOptions is not null) services.Remove(untypedOptions);

            services.AddDbContext<PropaneDriverDbContext>(options =>
                options.UseInMemoryDatabase(DatabaseName));

            // Program.cs reads Jwt:Key/Issuer/Audience and binds them onto
            // JwtBearerOptions before the factory's ConfigureAppConfiguration
            // callback layers in our test values — so the bearer middleware
            // would otherwise validate against the appsettings placeholder
            // while JwtTokenService (which reads config lazily at issuance
            // time) signs with our test key. Re-bind validation here so both
            // sides agree.
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
