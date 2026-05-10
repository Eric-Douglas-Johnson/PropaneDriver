using System.Net.Http.Headers;

namespace PropaneDriver.Client.Authentication
{
    // DelegatingHandler that attaches the persisted JWT (if any) as a Bearer
    // header on every outbound request. Reads from BrowserStorageService so a
    // page refresh keeps the user authenticated as long as the token's still
    // alive — Login/Logout update the stored value and the next request picks
    // it up automatically.
    public class BearerTokenHandler : DelegatingHandler
    {
        public const string TokenStorageKey = "auth_token";

        private readonly BrowserStorageService _browserStorageService;

        public BearerTokenHandler(BrowserStorageService browserStorageService)
        {
            _browserStorageService = browserStorageService;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization is null)
            {
                var bearerToken = await _browserStorageService.GetFromStorage<string>(TokenStorageKey);
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
