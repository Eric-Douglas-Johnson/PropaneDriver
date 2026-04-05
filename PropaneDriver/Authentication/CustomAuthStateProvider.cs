
using PropaneDriver.Dtos;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PropaneDriver.Authentication
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly string _userStorageKey = "user";
        private readonly string _authType = "UserAuthentication";
        private readonly string _openApiUri = "https://localhost:7217/openapi/internal.json";
        private readonly string _apiBaseUri = "http://localhost:7179/";
        private readonly BrowserStorageService _browserStorageService;
        private readonly HttpClient _httpClient;

        public UserDto CurrentUser { get; private set; } = new();
        private AuthenticationState EmptyAuthState = new (new ClaimsPrincipal());

        public CustomAuthStateProvider(BrowserStorageService storageService)
        {
            _browserStorageService = storageService;    
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(_apiBaseUri);
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
                var authResponseDto = ParseResponse(requestResultStr);

                //Role.NotAuthorized == 0
                if (authResponseDto.Role == 0)
                {
                    return new LoginStatus
                    {
                        Successful = false,
                        ErrorMessage = "Not Authorized"
                    };
                }

                List<Claim> claims;

                if (authResponseDto.Role == 1)
                {
                    var providerRequestResultStr = await SendProviderRequest(authResponseDto.UserId);
                    var provider = ParseProviderResult(providerRequestResultStr);
                    provider.Role = "provider";

                    claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, provider.Id.ToString()),
                        new Claim(ClaimTypes.Name, provider.UserName),
                        new Claim(ClaimTypes.Role, provider.Role)
                    };

                    await _browserStorageService.SaveToStorageAsync<ProviderDto>(_userStorageKey, provider);
                    CurrentUser = provider;
                }
                else
                {
                    var clientRequestResultStr = await SendClientRequest(authResponseDto.UserId);
                    var client = ParseClientResult(clientRequestResultStr);
                    client.Role = "client";

                    claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, client.Id.ToString()),
                        new Claim(ClaimTypes.Name, client.UserName),
                        new Claim(ClaimTypes.Role, client.Role)
                    };

                    await _browserStorageService.SaveToStorageAsync<ClientDto>(_userStorageKey, client);
                    CurrentUser = client;
                }

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

        private async Task<string> SendAuthRequest(CredsDto creds)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _apiBaseUri + "api/Authenticate");
            request.Content = new StringContent(JsonSerializer.Serialize(creds), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                return result;
            }
            else
            {
                throw new InvalidOperationException("Authorization Failed");
            }
        }

        private AuthResponseDto ParseResponse(string response)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var authDto = JsonSerializer.Deserialize<AuthResponseDto>(response, options);
            return authDto is null ? throw new InvalidOperationException("Could not parse json response") : authDto;
        }

        private async Task<string> SendProviderRequest(Guid providerId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _apiBaseUri + $"provider/{providerId}");
            using var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                return result;
            }
            else
            {
                throw new InvalidOperationException("Authorization Failed");
            }
        }

        private ProviderDto ParseProviderResult(string providerResponseStr)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var providerDto = JsonSerializer.Deserialize<ProviderDto>(providerResponseStr, options);
            return providerDto is null ? throw new InvalidOperationException("Could not parse json response for provider") : providerDto;
        }

        private async Task<string> SendClientRequest(Guid clientId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _apiBaseUri + $"client/{clientId}");
            using var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                return result;
            }
            else
            {
                throw new InvalidOperationException("Authorization Failed");
            }
        }

        private ClientDto ParseClientResult(string clientResponseStr)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var clientDto = JsonSerializer.Deserialize<ClientDto>(clientResponseStr, options);
            return clientDto is null ? throw new InvalidOperationException("Could not parse json response for client") : clientDto;
        }

        public async Task LogoutAsync()
        {
            await _browserStorageService.RemoveFromStorage(_userStorageKey);
            NotifyAuthenticationStateChanged(Task.FromResult(EmptyAuthState));
            CurrentUser = new EmptyUserDto();
        }

        public async Task<ProviderDto> RegisterNewClient(ProviderDto user, CredsDto creds)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                _apiBaseUri + $"client/register/{user.FirstName}/{user.MiddleName}/{user.LastName}/" +
                $"{user.Email}/{user.PhoneNumber}");

            request.Content = new StringContent(JsonSerializer.Serialize(creds), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return user;
            }
            else
            {
                throw new InvalidOperationException("Registration Failed");
            }
        }
    }
}
