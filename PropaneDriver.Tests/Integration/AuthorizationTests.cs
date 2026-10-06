using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PropaneDriver.Server.Data;
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
//   * SupervisorOrAdmin endpoints (drivers list, long-running) keep the
//     stricter "driver → 403" expectation; AdminOnly ones also reject
//     supervisors.
public class AuthorizationTests : IClassFixture<PropaneDriverWebAppFactory>
{
    private readonly PropaneDriverWebAppFactory _factory;

    public AuthorizationTests(PropaneDriverWebAppFactory factory)
    {
        _factory = factory;
    }

    // ---------- /api/drivers (SupervisorOrAdmin) ----------

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
        var driver = _factory.SeedUser("listdrivers-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListDrivers_AdminRole_Returns200()
    {
        var admin = _factory.SeedUser("listdrivers-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListDrivers_SupervisorRole_Returns200()
    {
        var supervisor = _factory.SeedUser("listdrivers-supervisor", role: "supervisor");
        using var client = _factory.CreateClientForUser(supervisor);
        var response = await client.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListDrivers_ExcludesUsersWithoutTheDriverRole()
    {
        var supervisor = _factory.SeedUser("listdrivers-only-supervisor", role: "supervisor");
        var driver = _factory.SeedUser("listdrivers-only-driver", role: "driver");
        using var client = _factory.CreateClientForUser(supervisor);

        var drivers = await client.GetFromJsonAsync<List<DriverDto>>("/api/drivers");

        Assert.Contains(drivers!, listed => listed.Id == driver.Id.ToString());
        Assert.DoesNotContain(drivers!, listed => listed.Id == supervisor.Id.ToString());
    }

    // ---------- GET /api/users (AdminOnly) ----------

    [Fact]
    public async Task ListUsers_SupervisorRole_Returns403()
    {
        var supervisor = _factory.SeedUser("listusers-supervisor", role: "supervisor");
        using var client = _factory.CreateClientForUser(supervisor);
        var response = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_AdminRole_ReturnsEveryAccountWithItsRoles()
    {
        var admin = _factory.SeedUser("listusers-admin", role: "admin");
        var supervisor = _factory.SeedUser("listusers-listed-supervisor", role: "supervisor");
        using var client = _factory.CreateClientForUser(admin);

        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users");

        Assert.Equal(["admin"], users!.Single(listed => listed.Id == admin.Id.ToString()).Roles);
        Assert.Equal(["supervisor"], users!.Single(listed => listed.Id == supervisor.Id.ToString()).Roles);
    }

    // ---------- POST /api/users (AdminOnly) ----------

    private static CreateUserDto NewUserRequest(string userName, params string[] roles) => new()
    {
        UserName = userName,
        Password = "new-user-password",
        FirstName = "New",
        LastName = "User",
        Roles = roles.Length == 0 ? ["driver"] : [.. roles]
    };

    [Fact]
    public async Task CreateUser_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/users", NewUserRequest("create-user-anonymous"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("supervisor")]
    public async Task CreateUser_NonAdminRole_Returns403(string callerRole)
    {
        var caller = _factory.SeedUser($"create-user-{callerRole}-caller", role: callerRole);
        using var client = _factory.CreateClientForUser(caller);
        var response = await client.PostAsJsonAsync(
            "/api/users", NewUserRequest($"create-user-by-{callerRole}", "admin"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_AdminRole_CreatesUserWhoCanSignIn()
    {
        var admin = _factory.SeedUser("create-user-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);

        var response = await client.PostAsJsonAsync(
            "/api/users", NewUserRequest("create-user-new-supervisor", "Supervisor"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(["supervisor"], created!.Roles);

        var signInResult = await SignInAsync("create-user-new-supervisor", "new-user-password");
        Assert.True(signInResult.IsAuthenticated);
        Assert.Equal(["supervisor"], signInResult.User!.Roles);
    }

    [Fact]
    public async Task CreateUser_SeveralRoles_SignsInWithAllOfThemInCanonicalOrder()
    {
        var admin = _factory.SeedUser("create-user-multi-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);

        var response = await client.PostAsJsonAsync(
            "/api/users", NewUserRequest("create-user-multi-role", "supervisor", "driver"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var signInResult = await SignInAsync("create-user-multi-role", "new-user-password");
        Assert.Equal(["driver", "supervisor"], signInResult.User!.Roles);
    }

    private async Task<AuthResponseDto> SignInAsync(string userName, string password)
    {
        using var anonymousClient = _factory.CreateAnonymousClient();
        var response = await anonymousClient.PostAsJsonAsync(
            "/api/Authenticate", new CredsDto { UserName = userName, Password = password });
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    [Fact]
    public async Task CreateUser_DuplicateUserName_Returns409()
    {
        var admin = _factory.SeedUser("create-user-dupe-admin", role: "admin");
        _factory.SeedUser("create-user-existing", role: "driver");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PostAsJsonAsync("/api/users", NewUserRequest("create-user-existing"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("create-user-bad-role", "new-user-password", "superuser")]
    [InlineData("create-user-short-password", "short", "driver")]
    [InlineData("", "new-user-password", "driver")]
    public async Task CreateUser_InvalidRequest_Returns400(string userName, string password, string role)
    {
        var admin = _factory.SeedUser($"create-user-invalid-admin-{Guid.NewGuid():N}", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var request = NewUserRequest(userName, role);
        request.Password = password;
        var response = await client.PostAsJsonAsync("/api/users", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_NoRoles_Returns400()
    {
        var admin = _factory.SeedUser("create-user-no-roles-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var request = NewUserRequest("create-user-no-roles");
        request.Roles = [];
        var response = await client.PostAsJsonAsync("/api/users", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- PUT /api/users/{id} (AdminOnly) ----------

    private static UserUpdateDto EditRequest(UserDbRecord target, string? userName = null, string role = "driver", string newPassword = "") => new()
    {
        UserName = userName ?? target.UserName,
        FirstName = "Edited",
        LastName = target.LastName,
        Email = target.Email,
        PhoneNumber = target.PhoneNumber,
        Roles = [role],
        NewPassword = newPassword
    };

    [Fact]
    public async Task UpdateUser_Anonymous_Returns401()
    {
        var target = _factory.SeedUser("edit-user-anonymous-target", role: "driver");
        using var client = _factory.CreateAnonymousClient();
        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}", EditRequest(target));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("supervisor")]
    public async Task UpdateUser_NonAdminRole_Returns403(string callerRole)
    {
        var caller = _factory.SeedUser($"edit-user-{callerRole}-caller", role: callerRole);
        var target = _factory.SeedUser($"edit-user-{callerRole}-target", role: "driver");
        using var client = _factory.CreateClientForUser(caller);
        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}", EditRequest(target, role: "admin"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_AdminRole_UpdatesProfileRoleAndPassword()
    {
        var admin = _factory.SeedUser("edit-user-admin", role: "admin");
        var target = _factory.SeedUser("edit-user-target", role: "driver");
        using var client = _factory.CreateClientForUser(admin);

        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}",
            EditRequest(target, userName: "edit-user-renamed", role: "supervisor", newPassword: "changed-password"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var signInResult = await SignInAsync("edit-user-renamed", "changed-password");
        Assert.True(signInResult.IsAuthenticated);
        Assert.Equal(["supervisor"], signInResult.User!.Roles);
        Assert.Equal("Edited", signInResult.User.FirstName);
    }

    [Fact]
    public async Task UpdateUser_RemovingDriverRoleWhileRoutesExist_Returns400AndKeepsRole()
    {
        var admin = _factory.SeedUser("edit-user-history-admin", role: "admin");
        var target = _factory.SeedUser("edit-user-history-target", role: "driver");
        _factory.SeedRoute(target.Id);
        using var client = _factory.CreateClientForUser(admin);

        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}", EditRequest(target, role: "supervisor"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        Assert.True(db.Drivers.Any(d => d.UserId == target.Id));
        Assert.False(db.Supervisors.Any(s => s.UserId == target.Id));
    }

    [Fact]
    public async Task UpdateUser_AddingSupervisorToDriverWithRoutes_KeepsBothRoles()
    {
        var admin = _factory.SeedUser("edit-user-promote-admin", role: "admin");
        var target = _factory.SeedUser("edit-user-promote-target", role: "driver");
        _factory.SeedRoute(target.Id);
        using var client = _factory.CreateClientForUser(admin);

        var request = EditRequest(target);
        request.Roles = ["driver", "supervisor"];
        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(["driver", "supervisor"], updated!.Roles);
    }

    [Fact]
    public async Task UpdateUser_BlankNewPassword_KeepsCurrentPassword()
    {
        var admin = _factory.SeedUser("edit-user-keep-password-admin", role: "admin");
        var target = _factory.SeedUser("edit-user-keep-password-target", role: "driver", password: "original-password");
        using var client = _factory.CreateClientForUser(admin);

        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}", EditRequest(target));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var signInResult = await SignInAsync(target.UserName, "original-password");
        Assert.True(signInResult.IsAuthenticated);
    }

    [Fact]
    public async Task UpdateUser_AdminEditingOwnProfile_Returns200()
    {
        var admin = _factory.SeedUser("edit-user-self-profile-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PutAsJsonAsync($"/api/users/{admin.Id}", EditRequest(admin, role: "admin"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_AdminChangingOwnRole_Returns400()
    {
        var admin = _factory.SeedUser("edit-user-self-role-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PutAsJsonAsync($"/api/users/{admin.Id}", EditRequest(admin, role: "driver"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_UnknownRole_Returns400()
    {
        var admin = _factory.SeedUser("edit-user-bad-role-admin", role: "admin");
        var target = _factory.SeedUser("edit-user-bad-role-target", role: "driver");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}", EditRequest(target, role: "superuser"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_UserNameTakenByAnotherUser_Returns409()
    {
        var admin = _factory.SeedUser("edit-user-dupe-admin", role: "admin");
        var target = _factory.SeedUser("edit-user-dupe-target", role: "driver");
        _factory.SeedUser("edit-user-dupe-existing", role: "driver");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}",
            EditRequest(target, userName: "edit-user-dupe-existing"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_UnknownUser_Returns404()
    {
        var admin = _factory.SeedUser("edit-user-missing-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PutAsJsonAsync($"/api/users/{Guid.NewGuid()}", EditRequest(admin));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /api/users/{id} (AdminOnly) ----------

    [Fact]
    public async Task DeleteUser_Anonymous_Returns401()
    {
        var target = _factory.SeedUser("delete-user-anonymous-target", role: "driver");
        using var client = _factory.CreateAnonymousClient();
        var response = await client.DeleteAsync($"/api/users/{target.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("supervisor")]
    public async Task DeleteUser_NonAdminRole_Returns403(string callerRole)
    {
        var caller = _factory.SeedUser($"delete-user-{callerRole}-caller", role: callerRole);
        var target = _factory.SeedUser($"delete-user-{callerRole}-target", role: "driver");
        using var client = _factory.CreateClientForUser(caller);
        var response = await client.DeleteAsync($"/api/users/{target.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_AdminRole_RemovesAccountRolesRoutesAndFuelLog()
    {
        var admin = _factory.SeedUser("delete-user-admin", role: "admin");
        var target = _factory.SeedUser("delete-user-target", role: "driver");
        _factory.SeedRoute(target.Id);
        _factory.SeedFuelLogEntry(target.Id);
        using var client = _factory.CreateClientForUser(admin);

        var response = await client.DeleteAsync($"/api/users/{target.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
        Assert.False(db.Users.Any(u => u.Id == target.Id));
        Assert.False(db.Drivers.Any(d => d.UserId == target.Id));
        Assert.False(db.Routes.Any(r => r.DriverId == target.Id));
        Assert.False(db.FuelLogEntries.Any(f => f.DriverId == target.Id));
    }

    [Fact]
    public async Task DeleteUser_AdminDeletingSelf_Returns400()
    {
        var admin = _factory.SeedUser("delete-user-self-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.DeleteAsync($"/api/users/{admin.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_UnknownUser_Returns404()
    {
        var admin = _factory.SeedUser("delete-user-missing-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.DeleteAsync($"/api/users/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Supervisor reaches route management, not admin-only data ----------

    [Fact]
    public async Task ListRoutesForDriver_SupervisorRole_CanReadAnyDriver()
    {
        var supervisor = _factory.SeedUser("route-list-supervisor", role: "supervisor");
        var someDriver = _factory.SeedUser("route-list-supervisor-target", role: "driver");
        using var client = _factory.CreateClientForUser(supervisor);
        var response = await client.GetAsync($"/api/routes/driver/{someDriver.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAllRoutesForDriver_DriverRole_Returns403()
    {
        var driver = _factory.SeedUser("route-delete-all-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.DeleteAsync($"/api/routes/driver/{driver.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAllRoutesForDriver_SupervisorRole_DeletesRoutes()
    {
        var supervisor = _factory.SeedUser("route-delete-all-supervisor", role: "supervisor");
        var someDriver = _factory.SeedUser("route-delete-all-target", role: "driver");
        _factory.SeedRoute(someDriver.Id);
        using var client = _factory.CreateClientForUser(supervisor);
        var response = await client.DeleteAsync($"/api/routes/driver/{someDriver.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FuelLogForDriver_SupervisorRole_Returns403()
    {
        var supervisor = _factory.SeedUser("fuel-log-supervisor", role: "supervisor");
        var someDriver = _factory.SeedUser("fuel-log-supervisor-target", role: "driver");
        using var client = _factory.CreateClientForUser(supervisor);
        var response = await client.GetAsync($"/api/fuel-log/{someDriver.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        var driver = _factory.SeedUser("self-lookup-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.GetAsync($"/driver/{driver.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDriverById_UserWithoutDriverRole_Returns404()
    {
        var supervisor = _factory.SeedUser("lookup-non-driver", role: "supervisor");
        using var client = _factory.CreateClientForUser(supervisor);
        var response = await client.GetAsync($"/driver/{supervisor.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var driver = _factory.SeedUser("route-list-self", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.GetAsync($"/api/routes/driver/{driver.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListRoutesForDriver_DriverRequestingOtherDriverRoutes_Returns403()
    {
        var requester = _factory.SeedUser("route-list-requester", role: "driver");
        var otherDriver = _factory.SeedUser("route-list-other", role: "driver");
        using var client = _factory.CreateClientForUser(requester);
        var response = await client.GetAsync($"/api/routes/driver/{otherDriver.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListRoutesForDriver_AdminRole_CanReadAnyDriver()
    {
        var admin = _factory.SeedUser("route-list-admin", role: "admin");
        var someDriver = _factory.SeedUser("route-list-target", role: "driver");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.GetAsync($"/api/routes/driver/{someDriver.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- GET /api/routes/{driverId}/{date} (self-or-admin) ----------

    [Fact]
    public async Task GetRouteByDriverAndDate_DriverRequestingOtherDriver_Returns403()
    {
        var requester = _factory.SeedUser("route-getbydate-requester", role: "driver");
        var otherDriver = _factory.SeedUser("route-getbydate-other", role: "driver");
        using var client = _factory.CreateClientForUser(requester);
        var response = await client.GetAsync($"/api/routes/{otherDriver.Id}/2026-01-15");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRouteByDriverAndDate_DriverRequestingOwn_PassesAuthFilter()
    {
        var driver = _factory.SeedUser("route-getbydate-self", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
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
        var driver = _factory.SeedUser("route-delete-self", role: "driver");
        var ownRoute = _factory.SeedRoute(driver.Id);
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.DeleteAsync($"/api/routes/{ownRoute.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoute_DriverDeletingOtherDriverRoute_Returns403()
    {
        var attacker = _factory.SeedUser("route-delete-attacker", role: "driver");
        var victim = _factory.SeedUser("route-delete-victim", role: "driver");
        var victimRoute = _factory.SeedRoute(victim.Id);
        using var client = _factory.CreateClientForUser(attacker);
        var response = await client.DeleteAsync($"/api/routes/{victimRoute.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoute_AdminRole_CanDeleteAnyRoute()
    {
        var admin = _factory.SeedUser("route-delete-admin", role: "admin");
        var driver = _factory.SeedUser("route-delete-target", role: "driver");
        var driverRoute = _factory.SeedRoute(driver.Id);
        using var client = _factory.CreateClientForUser(admin);
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
        var driver = _factory.SeedUser("route-create-self", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
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
        var attacker = _factory.SeedUser("route-create-attacker", role: "driver");
        var victim = _factory.SeedUser("route-create-victim", role: "driver");
        using var client = _factory.CreateClientForUser(attacker);
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
        var admin = _factory.SeedUser("route-create-admin", role: "admin");
        var driver = _factory.SeedUser("route-create-target", role: "driver");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto
        {
            DriverId = driver.Id.ToString(),
            Date = new DateOnly(2026, 1, 15),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateRoute_ForUserWithoutDriverRole_Returns400()
    {
        var admin = _factory.SeedUser("route-create-non-driver-admin", role: "admin");
        var supervisor = _factory.SeedUser("route-create-non-driver-target", role: "supervisor");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PostAsJsonAsync("/api/routes", new CreateRouteDto
        {
            DriverId = supervisor.Id.ToString(),
            Date = new DateOnly(2026, 1, 15),
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- POST /api/routes/{routeId}/deliveries (self-or-admin) ----------

    [Fact]
    public async Task AddDeliveryToRoute_DriverAddingToOwnRoute_PassesAuthFilter()
    {
        var driver = _factory.SeedUser("delivery-add-self", role: "driver");
        var ownRoute = _factory.SeedRoute(driver.Id);
        using var client = _factory.CreateClientForUser(driver);
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
        var attacker = _factory.SeedUser("delivery-add-attacker", role: "driver");
        var victim = _factory.SeedUser("delivery-add-victim", role: "driver");
        var victimRoute = _factory.SeedRoute(victim.Id);
        using var client = _factory.CreateClientForUser(attacker);
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
        var driver = _factory.SeedUser("import-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
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
        var driver = _factory.SeedUser("tools-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
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
        var admin = _factory.SeedUser("tools-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
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
        var driver = _factory.SeedUser("alert-delete-self", role: "driver");
        var route = _factory.SeedRoute(driver.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        var alert = _factory.SeedAlert(delivery.Id);
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.DeleteAsync($"/api/alerts/{alert.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAlert_DriverDeletingOtherDriverAlert_Returns403()
    {
        var attacker = _factory.SeedUser("alert-delete-attacker", role: "driver");
        var victim = _factory.SeedUser("alert-delete-victim", role: "driver");
        var route = _factory.SeedRoute(victim.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        var alert = _factory.SeedAlert(delivery.Id);
        using var client = _factory.CreateClientForUser(attacker);
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
        var driver = _factory.SeedUser("alert-seen-self", role: "driver");
        var route = _factory.SeedRoute(driver.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        var alert = _factory.SeedAlert(delivery.Id);
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.PutAsync($"/api/alerts/{alert.Id}/seen", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MarkAlertSeen_DriverOnOtherDriverAlert_Returns403()
    {
        var attacker = _factory.SeedUser("alert-seen-attacker", role: "driver");
        var victim = _factory.SeedUser("alert-seen-victim", role: "driver");
        var route = _factory.SeedRoute(victim.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        var alert = _factory.SeedAlert(delivery.Id);
        using var client = _factory.CreateClientForUser(attacker);
        var response = await client.PutAsync($"/api/alerts/{alert.Id}/seen", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- POST /api/deliveries/{id}/alerts (self-or-admin) ----------

    [Fact]
    public async Task CreateDeliveryAlert_DriverOnOwnDelivery_Returns200()
    {
        var driver = _factory.SeedUser("delivery-alert-self", role: "driver");
        var route = _factory.SeedRoute(driver.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{delivery.Id}/alerts",
            new CreateAlertDto { Message = "test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateDeliveryAlert_DriverOnOtherDriverDelivery_Returns403()
    {
        var attacker = _factory.SeedUser("delivery-alert-attacker", role: "driver");
        var victim = _factory.SeedUser("delivery-alert-victim", role: "driver");
        var route = _factory.SeedRoute(victim.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        using var client = _factory.CreateClientForUser(attacker);
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{delivery.Id}/alerts",
            new CreateAlertDto { Message = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateDeliveryAlert_AdminOnAnyDelivery_Returns200()
    {
        var admin = _factory.SeedUser("delivery-alert-admin", role: "admin");
        var driver = _factory.SeedUser("delivery-alert-target", role: "driver");
        var route = _factory.SeedRoute(driver.Id);
        var delivery = _factory.SeedDelivery(route.Id);
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{delivery.Id}/alerts",
            new CreateAlertDto { Message = "test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- PUT /api/deliveries/{id}/long-running (SupervisorOrAdmin) ----------

    [Fact]
    public async Task UpdateLongRunning_DriverRole_Returns403()
    {
        var driver = _factory.SeedUser("longrun-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);
        var response = await client.PutAsJsonAsync(
            $"/api/deliveries/{Guid.NewGuid()}/long-running",
            new DeliveryLongRunningUpdateDto { LongRunning = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLongRunning_AdminRole_PassesAuthFilter()
    {
        var admin = _factory.SeedUser("longrun-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);
        var response = await client.PutAsJsonAsync(
            $"/api/deliveries/{Guid.NewGuid()}/long-running",
            new DeliveryLongRunningUpdateDto { LongRunning = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLongRunning_SupervisorRole_PassesAuthFilter()
    {
        var supervisor = _factory.SeedUser("longrun-supervisor", role: "supervisor");
        using var client = _factory.CreateClientForUser(supervisor);
        var response = await client.PutAsJsonAsync(
            $"/api/deliveries/{Guid.NewGuid()}/long-running",
            new DeliveryLongRunningUpdateDto { LongRunning = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- /api/Authenticate (anonymous, returns token) ----------

    [Fact]
    public async Task Authenticate_ValidCreds_ReturnsTokenAndUser()
    {
        var driver = _factory.SeedUser("auth-flow-user", role: "admin", password: "auth-flow-pw");
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
        Assert.NotNull(auth.User);
        Assert.Equal(["admin"], auth.User!.Roles);
        Assert.Equal("auth-flow-user", auth.User.UserName);
    }

    [Fact]
    public async Task Authenticate_UserWithNoRoles_IsRefused()
    {
        _factory.SeedUser("auth-no-roles-user", role: null, password: "no-roles-pw");
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/Authenticate", new CredsDto
        {
            UserName = "auth-no-roles-user",
            Password = "no-roles-pw"
        });

        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.False(auth!.IsAuthenticated);
        Assert.True(string.IsNullOrWhiteSpace(auth.Token));
    }

    [Fact]
    public async Task Authenticate_TokenFromAuthEndpoint_OpensAdminOnlyEndpoint()
    {
        _factory.SeedUser("token-flow-admin", role: "admin", password: "token-flow-pw");
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
        _factory.SeedUser("bad-pw-user", role: "driver", password: "right-pw");
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
        Assert.Null(auth.User);
    }

    // ---------- GET /api/client-logs (AdminOnly) ----------

    [Fact]
    public async Task ListErrorLogs_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.GetAsync("/api/client-logs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("supervisor")]
    public async Task ListErrorLogs_NonAdminRole_Returns403(string callerRole)
    {
        var caller = _factory.SeedUser($"list-error-logs-{callerRole}", role: callerRole);
        using var client = _factory.CreateClientForUser(caller);
        var response = await client.GetAsync("/api/client-logs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListErrorLogs_AdminRole_ReturnsNewestFirstUpToCount()
    {
        // Far-future timestamps keep these two at the top of the shared test database's log.
        var newestTimestamp = new DateTime(2099, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
            db.ErrorLogs.AddRange(
                new ErrorLogDbRecord { Id = Guid.NewGuid(), Source = "list-error-logs-older", Level = "Error", Message = "older", Timestamp = newestTimestamp.AddDays(-1) },
                new ErrorLogDbRecord { Id = Guid.NewGuid(), Source = "list-error-logs-newer", Level = "Warning", Message = "newer", Timestamp = newestTimestamp });
            await db.SaveChangesAsync();
        }

        var admin = _factory.SeedUser("list-error-logs-admin", role: "admin");
        using var client = _factory.CreateClientForUser(admin);

        var topTwoLogs = await client.GetFromJsonAsync<List<ErrorLogEntryDto>>("/api/client-logs?count=2");
        Assert.Equal(["list-error-logs-newer", "list-error-logs-older"], topTwoLogs!.Select(log => log.Source));
        Assert.Equal("Warning", topTwoLogs![0].Level);
        Assert.Equal("newer", topTwoLogs![0].Message);

        var topLog = await client.GetFromJsonAsync<List<ErrorLogEntryDto>>("/api/client-logs?count=1");
        Assert.Equal("list-error-logs-newer", Assert.Single(topLog!).Source);
    }

    [Fact]
    public async Task PostClientLog_Anonymous_IsStillAccepted()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/client-logs", new ClientLogDto
        {
            Source = "post-client-log-anonymous",
            Level = "Error",
            Message = "raised before sign-in"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- GET /api/admin/schema (AdminOnly) ----------
    // No admin-passes case: the handler queries sys.columns, which the in-memory provider can't run.

    [Fact]
    public async Task SchemaDiagnostics_Anonymous_Returns401()
    {
        using var client = _factory.CreateAnonymousClient();
        var response = await client.GetAsync("/api/admin/schema");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("supervisor")]
    public async Task SchemaDiagnostics_NonAdminRole_Returns403(string callerRole)
    {
        var caller = _factory.SeedUser($"schema-diagnostics-{callerRole}", role: callerRole);
        using var client = _factory.CreateClientForUser(caller);
        var response = await client.GetAsync("/api/admin/schema");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
