using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace Nornis.Web.Authentication;

/// <summary>
/// Attaches the signed-in user's Auth0 access token (stored in the auth cookie via
/// SaveTokens) to every nornis-api request. In Blazor Server the SignalR connection's
/// HttpContext carries the cookie, so the accessor resolves during circuits too.
/// Near-expiry tokens are silently renewed by <see cref="Auth0TokenRefresher"/> so a
/// long-lived circuit keeps working past the 24h access-token lifetime. When no token
/// is available the request goes out anonymous — locally the API's dev bypass handles it.
/// </summary>
public class BearerTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Auth0TokenRefresher _tokenRefresher;

    public BearerTokenHandler(IHttpContextAccessor httpContextAccessor, Auth0TokenRefresher tokenRefresher)
    {
        _httpContextAccessor = httpContextAccessor;
        _tokenRefresher = tokenRefresher;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            var accessToken = await _tokenRefresher.GetAccessTokenAsync(httpContext, cancellationToken);
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
