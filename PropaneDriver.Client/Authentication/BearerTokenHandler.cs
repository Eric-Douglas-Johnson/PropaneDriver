using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace PropaneDriver.Client.Authentication
{
    // DelegatingHandler that attaches the persisted JWT (if any) as a Bearer
    // header on every outbound request. Reads from BrowserStorageService so a
    // page refresh keeps the user authenticated as long as the token's still
    // alive — Login/Logout update the stored value and the next request picks
    // it up automatically.
    //
    // Also catches 401 responses on requests we authenticated: that means the
    // token expired (or was revoked), so we clear the cached creds and bounce
    // the user back to the login page via a full reload. The reload causes
    // CustomAuthStateProvider to re-init as anonymous, which trips the existing
    // RedirectToLogin route guard.
    public class BearerTokenHandler : DelegatingHandler
    {
        public const string TokenStorageKey = "auth_token";

        private readonly BrowserStorageService _browserStorageService;
        private readonly NavigationManager _navigationManager;

        public BearerTokenHandler(
            BrowserStorageService browserStorageService,
            NavigationManager navigationManager)
        {
            _browserStorageService = browserStorageService;
            _navigationManager = navigationManager;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var attachedBearerToken = false;
            if (request.Headers.Authorization is null)
            {
                var bearerToken = await _browserStorageService.GetFromStorage<string>(TokenStorageKey);
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                    attachedBearerToken = true;
                }
            }

            var response = await base.SendAsync(request, cancellationToken);

            // Only treat a 401 as a session timeout if we actually sent a token —
            // a 401 on an anonymous request (e.g. a failed login attempt) just
            // means the credentials were wrong, not that an existing session
            // expired.
            if (response.StatusCode == HttpStatusCode.Unauthorized && attachedBearerToken)
            {
                await _browserStorageService.RemoveFromStorage(TokenStorageKey);
                await _browserStorageService.RemoveFromStorage(CustomAuthStateProvider.UserStorageKey);
                _navigationManager.NavigateTo("authentication/login", forceLoad: true);
            }

            return response;
        }
    }
}
