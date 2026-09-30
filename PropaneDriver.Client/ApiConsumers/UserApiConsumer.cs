using System.Net.Http.Json;
using System.Text.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.ApiConsumers
{
    // Admin account management against api/users.
    public class UserApiConsumer
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;

        public UserApiConsumer(HttpClient http)
        {
            _http = http;
        }

        // Every account, ordered by last then first name. Null when the request fails.
        public async Task<List<UserDto>?> GetUsersAsync()
        {
            try
            {
                return await _http.GetFromJsonAsync<List<UserDto>>("api/users") ?? new();
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(
                    "UserApiConsumer.GetUsersAsync",
                    $"Exception loading users: {ex.Message}");
                return null;
            }
        }

        // On a rejected request, ErrorMessage carries the server's reason.
        public async Task<(UserDto? CreatedUser, string? ErrorMessage)> CreateUserAsync(CreateUserDto newUser)
        {
            const string source = "UserApiConsumer.CreateUserAsync";
            try
            {
                var response = await _http.PostAsJsonAsync("api/users", newUser);

                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadFromJsonAsync<UserDto>(), null);

                return (null, await ReadAndLogErrorAsync(response, source, "POST api/users"));
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(source, $"Exception creating user {newUser.UserName}: {ex.Message}");
                return (null, ex.Message);
            }
        }

        // On a rejected request, ErrorMessage carries the server's reason.
        public async Task<(UserDto? UpdatedUser, string? ErrorMessage)> UpdateUserAsync(string userId, UserUpdateDto changes)
        {
            const string source = "UserApiConsumer.UpdateUserAsync";
            try
            {
                var response = await _http.PutAsJsonAsync($"api/users/{userId}", changes);

                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadFromJsonAsync<UserDto>(), null);

                return (null, await ReadAndLogErrorAsync(response, source, $"PUT api/users/{userId}"));
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(source, $"Exception updating user {userId}: {ex.Message}");
                return (null, ex.Message);
            }
        }

        // Deletes the account and everything its roles own.
        public async Task<(bool Deleted, string? ErrorMessage)> DeleteUserAsync(string userId)
        {
            const string source = "UserApiConsumer.DeleteUserAsync";
            try
            {
                var response = await _http.DeleteAsync($"api/users/{userId}");

                if (response.IsSuccessStatusCode)
                    return (true, null);

                return (false, await ReadAndLogErrorAsync(response, source, $"DELETE api/users/{userId}"));
            }
            catch (Exception ex)
            {
                await ErrorLogApiConsumer.LogErrorAsync(source, $"Exception deleting user {userId}: {ex.Message}");
                return (false, ex.Message);
            }
        }

        // Logs a failed response and returns the server's { message } text, or a generic fallback.
        private static async Task<string> ReadAndLogErrorAsync(HttpResponseMessage response, string source, string request)
        {
            var body = await response.Content.ReadAsStringAsync();
            await ErrorLogApiConsumer.LogErrorAsync(source, $"{request} returned {(int)response.StatusCode}: {body}");

            string? serverMessage = null;
            try { serverMessage = JsonSerializer.Deserialize<ApiErrorBody>(body, JsonOptions)?.Message; }
            catch (JsonException) { }

            return serverMessage ?? $"Request failed ({(int)response.StatusCode}).";
        }

        private sealed record ApiErrorBody(string? Message);
    }
}
