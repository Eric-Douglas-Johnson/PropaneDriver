using PropaneDriver.Shared.Dtos;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PropaneDriver.Client.Authentication
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly string _userStorageKey = "user";
        private readonly string _authType = "UserAuthentication";
        private readonly BrowserStorageService _browserStorageService;
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public UserDto CurrentUser { get; private set; } = new();
        private AuthenticationState EmptyAuthState = new(new ClaimsPrincipal());

        public CustomAuthStateProvider(BrowserStorageService storageService, HttpClient httpClient)
        {
            _browserStorageService = storageService;
            _httpClient = httpClient;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var user = await _browserStorageService.GetFromStorage<UserDto?>(_userStorageKey);

            if (user is null)
            {
                return EmptyAuthState;
            }
            else
            {
                CurrentUser = user;

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(ClaimTypes.Role, user.Role)
                };

                var identity = new ClaimsIdentity(claims, _authType);
                var principal = new ClaimsPrincipal(identity);
                var authState = new AuthenticationState(principal);
                return authState;
            }
        }

        public async Task<LoginStatus> LoginAsync(CredsDto creds)
        {
            try
            {
                var requestResultStr = await SendAuthRequest(creds);
                var authResponseDto = Deserialize<AuthResponseDto>(requestResultStr);

                //Role.NotAuthorized == 0
                if (authResponseDto.Role == 0)
                {
                    return new LoginStatus
                    {
                        Successful = false,
                        ErrorMessage = "Not Authorized"
                    };
                }

                var driverResultStr = await SendGetRequest($"driver/{authResponseDto.UserId}");
                var driver = Deserialize<DriverDto>(driverResultStr);
                driver.Role = "driver";

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, driver.Id.ToString()),
                    new Claim(ClaimTypes.Name, driver.UserName),
                    new Claim(ClaimTypes.Role, driver.Role)
                };

                await _browserStorageService.SaveToStorageAsync(_userStorageKey, driver);
                CurrentUser = driver;

                var identity = new ClaimsIdentity(claims, _authType);
                var principal = new ClaimsPrincipal(identity);
                var authState = new AuthenticationState(principal);

                NotifyAuthenticationStateChanged(Task.FromResult(authState));

                return new LoginStatus
                {
                    Successful = true
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

                return new LoginStatus
                {
                    Successful = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task LogoutAsync()
        {
            await _browserStorageService.RemoveFromStorage(_userStorageKey);
            NotifyAuthenticationStateChanged(Task.FromResult(EmptyAuthState));
            CurrentUser = new UserDto();
        }

        private async Task<string> SendAuthRequest(CredsDto creds)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/Authenticate");
            request.Content = new StringContent(JsonSerializer.Serialize(creds), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                throw new InvalidOperationException("Authorization Failed");
            }
        }

        private async Task<string> SendGetRequest(string endpoint)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                throw new InvalidOperationException($"Request to {endpoint} failed");
            }
        }

        private static T Deserialize<T>(string json)
        {
            var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            return result ?? throw new InvalidOperationException($"Could not parse JSON response as {typeof(T).Name}");
        }
    }
}
