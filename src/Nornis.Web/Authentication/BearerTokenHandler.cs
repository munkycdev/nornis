using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace Nornis.Web.Authentication;

/// <summary>
/// Attaches the signed-in user's Auth0 access token (stored in the auth cookie via
/// SaveTokens) to every nornis-api request. In Blazor Server the SignalR connection's
/// HttpContext carries the cookie, so the accessor resolves during circuits too.
/// When no token is available the request goes out anonymous — locally the API's
/// dev bypass handles it.
/// </summary>
public class BearerTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BearerTokenHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            var accessToken = await httpContext.GetTokenAsync("access_token");
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
