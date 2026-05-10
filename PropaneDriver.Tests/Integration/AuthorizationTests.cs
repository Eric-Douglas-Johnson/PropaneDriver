using System.Net;
using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Tests.Integration;

// End-to-end checks that the [RequireAuthorization] policies on each
// endpoint actually close the gate. We're not testing the handler logic
// here (existing per-endpoint tests cover that) — only that:
//   * anonymous → 401
//   * driver-role JWT → 403 against AdminOnly endpoints
//   * admin-role JWT → succeeds (any 2xx; some endpoints will return 4xx
//     for body-shape reasons, which is fine — we just want to prove the
//     auth filter let the request through)
public class AuthorizationTests : IClassFixture<PropaneDriverWebAppFactory>
{
    private readonly PropaneDriverWebAppFactory _factory;

    public AuthorizationTests(PropaneDriverWebAppFactory factory)
    {
        _factory = factory;
    }

    // ---------- /api/drivers (AdminOnly) ----------

    [Fact]
    public async Task ListDrivers_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListDrivers_DriverRole_Returns403()
    {
        var driver = _factory.SeedDriver("listdrivers-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListDrivers_AdminRole_Returns200()
    {
        var admin = _factory.SeedDriver("listdrivers-admin", role: "admin");
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- /driver/{id} (AuthenticatedDriver) ----------

    [Fact]
    public async Task GetDriverById_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.GetAsync($"/driver/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDriverById_DriverRole_Authorized()
    {
        var driver = _factory.SeedDriver("self-lookup-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.GetAsync($"/driver/{driver.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- /api/routes/driver/{driverId} (AdminOnly) ----------

    [Fact]
    public async Task ListRoutesForDriver_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.GetAsync($"/api/routes/driver/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListRoutesForDriver_DriverRole_Returns403()
    {
        var driver = _factory.SeedDriver("route-list-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.GetAsync($"/api/routes/driver/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListRoutesForDriver_AdminRole_Returns200()
    {
        var admin = _factory.SeedDriver("route-list-admin", role: "admin");
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.GetAsync($"/api/routes/driver/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- DELETE /api/routes/{id} (AdminOnly) ----------

    [Fact]
    public async Task DeleteRoute_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.DeleteAsync($"/api/routes/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoute_DriverRole_Returns403()
    {
        var driver = _factory.SeedDriver("route-delete-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.DeleteAsync($"/api/routes/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoute_AdminRole_PassesAuthFilter()
    {
        var admin = _factory.SeedDriver("route-delete-admin", role: "admin");
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.DeleteAsync($"/api/routes/{Guid.NewGuid()}");
        // Route doesn't exist → 404 from the handler. Anything other than
        // 401/403 proves the AdminOnly filter let us through.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- POST /api/routes (AdminOnly) ----------

    [Fact]
    public async Task CreateRoute_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateRoute_DriverRole_Returns403()
    {
        var driver = _factory.SeedDriver("route-create-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateRoute_AdminRole_PassesAuthFilter()
    {
        var admin = _factory.SeedDriver("route-create-admin", role: "admin");
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto());
        // Empty DTO → handler validates DriverId → 400. Proves the filter
        // let us reach the handler.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- POST /api/imports/dispatch-screenshot (AdminOnly) ----------

    [Fact]
    public async Task DispatchImport_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
        }, "file", "fake.png");
        var response = await client.PostAsync("/api/imports/dispatch-screenshot", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DispatchImport_DriverRole_Returns403()
    {
        var driver = _factory.SeedDriver("import-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
        }, "file", "fake.png");
        var response = await client.PostAsync("/api/imports/dispatch-screenshot", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- DELETE /api/alerts/{id} (AdminOnly) + PUT seen (AuthenticatedDriver) ----------

    [Fact]
    public async Task DeleteAlert_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.DeleteAsync($"/api/alerts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAlert_DriverRole_Returns403()
    {
        var driver = _factory.SeedDriver("alert-delete-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.DeleteAsync($"/api/alerts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MarkAlertSeen_DriverRole_PassesAuthFilter()
    {
        var driver = _factory.SeedDriver("alert-seen-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.PutAsync($"/api/alerts/{Guid.NewGuid()}/seen", null);
        // No alert with that id → 404. Proves AuthenticatedDriver passed.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarkAlertSeen_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.PutAsync($"/api/alerts/{Guid.NewGuid()}/seen", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- POST /api/deliveries/{id}/alerts (AdminOnly) ----------

    [Fact]
    public async Task CreateDeliveryAlert_DriverRole_Returns403()
    {
        var driver = _factory.SeedDriver("delivery-alert-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{Guid.NewGuid()}/alerts",
            new CreateAlertDto { Message = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateDeliveryAlert_AdminRole_PassesAuthFilter()
    {
        var admin = _factory.SeedDriver("delivery-alert-admin", role: "admin");
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{Guid.NewGuid()}/alerts",
            new CreateAlertDto { Message = "test" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /api/addresses/{id}/long-running (AdminOnly) ----------

    [Fact]
    public async Task UpdateLongRunning_DriverRole_Returns403()
    {
        var driver = _factory.SeedDriver("longrun-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.PutAsJsonAsync(
            $"/api/addresses/{Guid.NewGuid()}/long-running",
            new AddressLongRunningUpdateDto { LongRunning = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLongRunning_AdminRole_PassesAuthFilter()
    {
        var admin = _factory.SeedDriver("longrun-admin", role: "admin");
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.PutAsJsonAsync(
            $"/api/addresses/{Guid.NewGuid()}/long-running",
            new AddressLongRunningUpdateDto { LongRunning = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- /api/Authenticate (anonymous, returns token) ----------

    [Fact]
    public async Task Authenticate_ValidCreds_ReturnsTokenAndDriver()
    {
        var driver = _factory.SeedDriver("auth-flow-user", role: "admin", password: "auth-flow-pw");
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/Authenticate", new CredsDto
        {
            UserName = "auth-flow-user",
            Password = "auth-flow-pw"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        Assert.True(auth!.IsAuthenticated);
        Assert.Equal(driver.Id, auth.UserId);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
        Assert.NotNull(auth.Driver);
        Assert.Equal("admin", auth.Driver!.Role);
        Assert.Equal("auth-flow-user", auth.Driver.UserName);
    }

    [Fact]
    public async Task Authenticate_TokenFromAuthEndpoint_OpensAdminOnlyEndpoint()
    {
        _factory.SeedDriver("token-flow-admin", role: "admin", password: "token-flow-pw");
        using var anonymous = _factory.CreateAnonymousClient();

        var loginResponse = await anonymous.PostAsJsonAsync("/api/Authenticate", new CredsDto
        {
            UserName = "token-flow-admin",
            Password = "token-flow-pw"
        });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth?.Token);

        // Use the freshly-issued token as the bearer credential.
        using var authedClient = _factory.CreateAnonymousClient();
        authedClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.Token);

        var driversResponse = await authedClient.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.OK, driversResponse.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WrongPassword_ReturnsAuthenticatedFalseAndNoToken()
    {
        _factory.SeedDriver("bad-pw-user", role: "driver", password: "right-pw");
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/Authenticate", new CredsDto
        {
            UserName = "bad-pw-user",
            Password = "wrong-pw"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        Assert.False(auth!.IsAuthenticated);
        Assert.True(string.IsNullOrWhiteSpace(auth.Token));
        Assert.Null(auth.Driver);
    }

    // ---------- malformed token ----------

    [Fact]
    public async Task ListDrivers_GarbageBearerToken_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "this-is-not-a-jwt");
        var response = await client.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
