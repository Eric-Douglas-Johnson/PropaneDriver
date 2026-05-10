using System.Net;
using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Tests.Integration;

// End-to-end checks that the [RequireAuthorization] policies and inline
// ownership checks on each endpoint actually close the gate. We're not
// re-testing handler logic here — only the auth pipeline. For each
// relaxed endpoint we cover:
//   * anonymous → 401
//   * driver acting on someone else's data → 403
//   * driver acting on their own data → 200 (or downstream 4xx that proves
//     the auth filter let the request through)
//   * AdminOnly endpoints (drivers list, address long-running) keep the
//     stricter "driver → 403" expectation.
public class AuthorizationTests : IClassFixture<PropaneDriverWebAppFactory>
{
    private readonly PropaneDriverWebAppFactory _factory;

    public AuthorizationTests(PropaneDriverWebAppFactory factory)
    {
        _factory = factory;
    }

    // ---------- /api/drivers (AdminOnly — stays admin-only) ----------

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

    // ---------- /api/routes/driver/{driverId} (self-or-admin) ----------

    [Fact]
    public async Task ListRoutesForDriver_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.GetAsync($"/api/routes/driver/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListRoutesForDriver_DriverRequestingOwnRoutes_Returns200()
    {
        var driver = _factory.SeedDriver("route-list-self", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.GetAsync($"/api/routes/driver/{driver.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListRoutesForDriver_DriverRequestingOtherDriverRoutes_Returns403()
    {
        var requester = _factory.SeedDriver("route-list-requester", role: "driver");
        var otherDriver = _factory.SeedDriver("route-list-other", role: "driver");
        using var client = _factory.CreateClientForDriver(requester);
        var response = await client.GetAsync($"/api/routes/driver/{otherDriver.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListRoutesForDriver_AdminRole_CanReadAnyDriver()
    {
        var admin = _factory.SeedDriver("route-list-admin", role: "admin");
        var someDriver = _factory.SeedDriver("route-list-target", role: "driver");
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.GetAsync($"/api/routes/driver/{someDriver.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- GET /api/routes/{driverId}/{date} (self-or-admin) ----------

    [Fact]
    public async Task GetRouteByDriverAndDate_DriverRequestingOtherDriver_Returns403()
    {
        var requester = _factory.SeedDriver("route-getbydate-requester", role: "driver");
        var otherDriver = _factory.SeedDriver("route-getbydate-other", role: "driver");
        using var client = _factory.CreateClientForDriver(requester);
        var response = await client.GetAsync($"/api/routes/{otherDriver.Id}/2026-01-15");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRouteByDriverAndDate_DriverRequestingOwn_PassesAuthFilter()
    {
        var driver = _factory.SeedDriver("route-getbydate-self", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.GetAsync($"/api/routes/{driver.Id}/2026-01-15");
        // No route for that date → 404 from the handler. Anything other
        // than 401/403 proves auth + ownership passed.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /api/routes/{id} (self-or-admin) ----------

    [Fact]
    public async Task DeleteRoute_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.DeleteAsync($"/api/routes/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoute_DriverDeletingOwnRoute_Returns200()
    {
        var driver = _factory.SeedDriver("route-delete-self", role: "driver");
        var ownRoute = _factory.SeedRoute(driver.Id);
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.DeleteAsync($"/api/routes/{ownRoute.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoute_DriverDeletingOtherDriverRoute_Returns403()
    {
        var attacker = _factory.SeedDriver("route-delete-attacker", role: "driver");
        var victim = _factory.SeedDriver("route-delete-victim", role: "driver");
        var victimRoute = _factory.SeedRoute(victim.Id);
        using var client = _factory.CreateClientForDriver(attacker);
        var response = await client.DeleteAsync($"/api/routes/{victimRoute.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoute_AdminRole_CanDeleteAnyRoute()
    {
        var admin = _factory.SeedDriver("route-delete-admin", role: "admin");
        var driver = _factory.SeedDriver("route-delete-target", role: "driver");
        var driverRoute = _factory.SeedRoute(driver.Id);
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.DeleteAsync($"/api/routes/{driverRoute.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- POST /api/routes (self-or-admin) ----------

    [Fact]
    public async Task CreateRoute_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateRoute_DriverWithOwnDriverId_PassesAuthFilter()
    {
        var driver = _factory.SeedDriver("route-create-self", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto
        {
            DriverId = driver.Id.ToString(),
            Date = new DateOnly(2026, 1, 15),
            // Empty deliveries list — handler accepts it. Auth passed.
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateRoute_DriverSpoofingAnotherDriverId_Returns403()
    {
        var attacker = _factory.SeedDriver("route-create-attacker", role: "driver");
        var victim = _factory.SeedDriver("route-create-victim", role: "driver");
        using var client = _factory.CreateClientForDriver(attacker);
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto
        {
            DriverId = victim.Id.ToString(), // attempt to create on someone else's behalf
            Date = new DateOnly(2026, 1, 15),
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateRoute_AdminCanCreateForAnyDriver()
    {
        var admin = _factory.SeedDriver("route-create-admin", role: "admin");
        var driver = _factory.SeedDriver("route-create-target", role: "driver");
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto
        {
            DriverId = driver.Id.ToString(),
            Date = new DateOnly(2026, 1, 15),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- POST /api/routes/{routeId}/deliveries (self-or-admin) ----------

    [Fact]
    public async Task AddDeliveryToRoute_DriverAddingToOwnRoute_PassesAuthFilter()
    {
        var driver = _factory.SeedDriver("delivery-add-self", role: "driver");
        var ownRoute = _factory.SeedRoute(driver.Id);
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.PostAsJsonAsync($"/api/routes/{ownRoute.Id}/deliveries", new CreateDeliveryDto
        {
            CustomerName = "Test",
            Street = "123 Main St",
            City = "Testville",
            State = "MN",
            ZipCode = "55001",
        });
        // The handler uses EF.Functions.Collate (SQL Server only) inside
        // the address upsert, so on InMemory it 500s. Auth filter is what
        // we're proving here — anything other than 401/403 means it
        // reached the handler.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddDeliveryToRoute_DriverAddingToOtherDriverRoute_Returns403()
    {
        var attacker = _factory.SeedDriver("delivery-add-attacker", role: "driver");
        var victim = _factory.SeedDriver("delivery-add-victim", role: "driver");
        var victimRoute = _factory.SeedRoute(victim.Id);
        using var client = _factory.CreateClientForDriver(attacker);
        var response = await client.PostAsJsonAsync($"/api/routes/{victimRoute.Id}/deliveries", new CreateDeliveryDto
        {
            CustomerName = "Hostile Insert",
            Street = "456 Elm St",
            City = "Testville",
            State = "MN",
            ZipCode = "55001",
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- POST /api/imports/dispatch-screenshot (AuthenticatedDriver) ----------

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
    public async Task DispatchImport_DriverRole_PassesAuthFilter()
    {
        // Drivers can OCR their own dispatch screenshots from the
        // Dispatch page. The endpoint doesn't touch driver-specific data,
        // so just being authenticated is enough. Downstream OCR will
        // fail (502) since the test config points to a fake endpoint —
        // that's fine, we only care that 401/403 don't fire.
        var driver = _factory.SeedDriver("import-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
        }, "file", "fake.png");
        var response = await client.PostAsync("/api/imports/dispatch-screenshot", content);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- POST /api/imports/tools-document (AdminOnly) ----------

    [Fact]
    public async Task ToolsDocument_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
        }, "file", "fake.png");
        var response = await client.PostAsync("/api/imports/tools-document", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ToolsDocument_DriverRole_Returns403()
    {
        // The tools-document endpoint is the OCR backbone of the admin
        // Tools page, so a plain driver token must be denied.
        var driver = _factory.SeedDriver("tools-driver", role: "driver");
        using var client = _factory.CreateClientForDriver(driver);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
        }, "file", "fake.png");
        var response = await client.PostAsync("/api/imports/tools-document", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ToolsDocument_AdminRole_PassesAuthFilter()
    {
        // Downstream OCR will fail (502) against the fake Doc Intel
        // endpoint configured for tests — that's fine, we only care
        // that auth/role checks let admins through.
        var admin = _factory.SeedDriver("tools-admin", role: "admin");
        using var client = _factory.CreateClientForDriver(admin);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
        }, "file", "fake.png");
        var response = await client.PostAsync("/api/imports/tools-document", content);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- DELETE /api/alerts/{id} (self-or-admin via route ownership) ----------

    [Fact]
    public async Task DeleteAlert_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.DeleteAsync($"/api/alerts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAlert_DriverDeletingOwnAlert_Returns200()
    {
        var driver = _factory.SeedDriver("alert-delete-self", role: "driver");
        var route = _factory.SeedRoute(driver.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        var alert = _factory.SeedAlert(delivery.Id);
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.DeleteAsync($"/api/alerts/{alert.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAlert_DriverDeletingOtherDriverAlert_Returns403()
    {
        var attacker = _factory.SeedDriver("alert-delete-attacker", role: "driver");
        var victim = _factory.SeedDriver("alert-delete-victim", role: "driver");
        var route = _factory.SeedRoute(victim.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        var alert = _factory.SeedAlert(delivery.Id);
        using var client = _factory.CreateClientForDriver(attacker);
        var response = await client.DeleteAsync($"/api/alerts/{alert.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- PUT /api/alerts/{id}/seen (self-or-admin) ----------

    [Fact]
    public async Task MarkAlertSeen_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.PutAsync($"/api/alerts/{Guid.NewGuid()}/seen", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkAlertSeen_DriverOnOwnAlert_Returns200()
    {
        var driver = _factory.SeedDriver("alert-seen-self", role: "driver");
        var route = _factory.SeedRoute(driver.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        var alert = _factory.SeedAlert(delivery.Id);
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.PutAsync($"/api/alerts/{alert.Id}/seen", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MarkAlertSeen_DriverOnOtherDriverAlert_Returns403()
    {
        var attacker = _factory.SeedDriver("alert-seen-attacker", role: "driver");
        var victim = _factory.SeedDriver("alert-seen-victim", role: "driver");
        var route = _factory.SeedRoute(victim.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        var alert = _factory.SeedAlert(delivery.Id);
        using var client = _factory.CreateClientForDriver(attacker);
        var response = await client.PutAsync($"/api/alerts/{alert.Id}/seen", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- POST /api/deliveries/{id}/alerts (self-or-admin) ----------

    [Fact]
    public async Task CreateDeliveryAlert_DriverOnOwnDelivery_Returns200()
    {
        var driver = _factory.SeedDriver("delivery-alert-self", role: "driver");
        var route = _factory.SeedRoute(driver.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        using var client = _factory.CreateClientForDriver(driver);
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{delivery.Id}/alerts",
            new CreateAlertDto { Message = "test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateDeliveryAlert_DriverOnOtherDriverDelivery_Returns403()
    {
        var attacker = _factory.SeedDriver("delivery-alert-attacker", role: "driver");
        var victim = _factory.SeedDriver("delivery-alert-victim", role: "driver");
        var route = _factory.SeedRoute(victim.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        using var client = _factory.CreateClientForDriver(attacker);
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{delivery.Id}/alerts",
            new CreateAlertDto { Message = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateDeliveryAlert_AdminOnAnyDelivery_Returns200()
    {
        var admin = _factory.SeedDriver("delivery-alert-admin", role: "admin");
        var driver = _factory.SeedDriver("delivery-alert-target", role: "driver");
        var route = _factory.SeedRoute(driver.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        using var client = _factory.CreateClientForDriver(admin);
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{delivery.Id}/alerts",
            new CreateAlertDto { Message = "test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- PUT /api/addresses/{id}/long-running (AdminOnly — stays admin-only) ----------

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
