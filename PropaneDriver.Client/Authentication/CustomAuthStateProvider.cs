using PropaneDriver.Shared.Dtos;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PropaneDriver.Client.Authentication
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        public const string UserStorageKey = "user";
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
            var user = await _browserStorageService.GetFromStorage<UserDto?>(UserStorageKey);

            if (user is null)
            {
                return EmptyAuthState;
            }

            CurrentUser = user;

            var claims = BuildClaims(user);
            var identity = new ClaimsIdentity(claims, _authType);
            var principal = new ClaimsPrincipal(identity);
            return new AuthenticationState(principal);
        }

        public async Task<LoginStatus> LoginAsync(CredsDto creds)
        {
            try
            {
                var requestResultStr = await SendAuthRequest(creds);
                var authResponseDto = Deserialize<AuthResponseDto>(requestResultStr);

                if (!authResponseDto.IsAuthenticated || authResponseDto.Driver is null)
                {
                    return new LoginStatus
                    {
                        Successful = false,
                        ErrorMessage = string.IsNullOrWhiteSpace(authResponseDto.StatusMessage)
                            ? "Not Authorized"
                            : authResponseDto.StatusMessage
                    };
                }

                // Persist both the JWT (used by BearerTokenHandler on every
                // subsequent request) and the driver profile (used to rebuild
                // claims on a page refresh without an extra round-trip).
                await _browserStorageService.SaveToStorageAsync(
                    BearerTokenHandler.TokenStorageKey, authResponseDto.Token);
                await _browserStorageService.SaveToStorageAsync(UserStorageKey, authResponseDto.Driver);

                CurrentUser = authResponseDto.Driver;

                var identity = new ClaimsIdentity(BuildClaims(authResponseDto.Driver), _authType);
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

        public async Task<LoginStatus> RegisterDriverAsync(RegisterDriverDto registration)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/Register");
                request.Content = new StringContent(
                    JsonSerializer.Serialize(registration), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    return new LoginStatus
                    {
                        Successful = false,
                        ErrorMessage = "Registration failed"
                    };
                }

                return new LoginStatus { Successful = true };
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
            await _browserStorageService.RemoveFromStorage(UserStorageKey);
            await _browserStorageService.RemoveFromStorage(BearerTokenHandler.TokenStorageKey);
            NotifyAuthenticationStateChanged(Task.FromResult(EmptyAuthState));
            CurrentUser = new UserDto();
        }

        private static List<Claim> BuildClaims(UserDto user) => new()
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName),
            new Claim(ClaimTypes.Role, string.IsNullOrWhiteSpace(user.Role) ? "driver" : user.Role)
        };

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

        private static T Deserialize<T>(string json)
        {
            var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            return result ?? throw new InvalidOperationException($"Could not parse JSON response as {typeof(T).Name}");
        }
    }
}
