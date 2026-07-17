using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Nornis.Web.Authentication;

/// <summary>
/// Silently renews Auth0 access tokens using the refresh token requested via the
/// offline_access scope, so sessions outlive the 24-hour access-token lifetime.
///
/// Two cooperating paths share the same refresh core:
/// - Cookie path (<see cref="ValidatePrincipalAsync"/>): on every full HTTP request the
///   cookie middleware revalidates the principal; a near-expiry token is refreshed and
///   the new tokens are written back into the cookie (ShouldRenew). An expired token
///   that cannot be refreshed rejects the principal — a clean re-login, exactly today's
///   behavior, never a half-dead session.
/// - Circuit path (<see cref="GetAccessTokenAsync"/>, used by BearerTokenHandler): a
///   long-lived Blazor circuit makes no full HTTP requests, and a cookie cannot be
///   rewritten over an established SignalR connection — so refreshed tokens are held in
///   a per-user in-memory cache and the cookie catches up on the next page load.
///
/// Refresh tokens are non-rotating (server-side confidential client), which keeps
/// concurrent refreshes from different circuits race-free.
/// </summary>
public sealed class Auth0TokenRefresher
{
    /// <summary>Refresh when this close to expiry — early enough that an API call in
    /// flight never carries a token that dies mid-request.</summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(30);

    private sealed record CachedTokens(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);

    private readonly ConcurrentDictionary<string, CachedTokens> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<Auth0TokenRefresher> _logger;
    private readonly string? _domain;
    private readonly string? _clientId;
    private readonly string? _clientSecret;

    public Auth0TokenRefresher(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<Auth0TokenRefresher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _domain = configuration["Auth0:Domain"];
        _clientId = configuration["Auth0:ClientId"];
        _clientSecret = configuration["Auth0:ClientSecret"];
    }

    private bool CanRefresh => !string.IsNullOrEmpty(_domain) && !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret);

    // ------------------------------------------------------------- circuit path --

    /// <summary>
    /// A valid access token for the current user: the cached refreshed one, the cookie's
    /// stored one while it is still fresh, or a newly refreshed one. Returns the stored
    /// (possibly stale) token on refresh failure — the API's 401 then surfaces normally.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(HttpContext httpContext, CancellationToken ct)
    {
        var stored = await ReadStoredTokensAsync(httpContext);
        if (stored is null)
        {
            return null; // anonymous (local dev) — unchanged behavior
        }

        var sub = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.User.FindFirstValue("sub");

        // A circuit-refreshed token supersedes the (older) one in the cookie.
        if (sub is not null && _cache.TryGetValue(sub, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow + RefreshWindow)
        {
            return cached.AccessToken;
        }

        if (stored.ExpiresAt > DateTimeOffset.UtcNow + RefreshWindow)
        {
            return stored.AccessToken;
        }

        if (!CanRefresh || sub is null || string.IsNullOrEmpty(stored.RefreshToken))
        {
            return stored.AccessToken;
        }

        // Single-flight per user: concurrent API calls near expiry refresh once.
        var gate = _refreshLocks.GetOrAdd(sub, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(sub, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow + RefreshWindow)
            {
                return cached.AccessToken; // another caller already refreshed
            }

            var refreshToken = cached?.RefreshToken ?? stored.RefreshToken;
            var refreshed = await RefreshAsync(refreshToken, ct);
            if (refreshed is null)
            {
                return stored.AccessToken;
            }

            _cache[sub] = refreshed;
            return refreshed.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    // -------------------------------------------------------------- cookie path --

    /// <summary>
    /// Cookie revalidation hook: refreshes a near-expiry access token and persists the
    /// new tokens into the cookie. Rejects the principal only when the token is already
    /// expired and cannot be refreshed — forcing the same clean re-login as before.
    /// </summary>
    public async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var expiresAtRaw = context.Properties.GetTokenValue("expires_at");
        var refreshToken = context.Properties.GetTokenValue("refresh_token");

        if (expiresAtRaw is null
            || !DateTimeOffset.TryParse(expiresAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
        {
            return; // no tokens in this cookie — nothing to manage
        }

        var sub = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Principal?.FindFirstValue("sub");

        // A circuit refresh may have newer tokens than the cookie — sync them in.
        if (sub is not null && _cache.TryGetValue(sub, out var cached) && cached.ExpiresAt > expiresAt)
        {
            WriteTokens(context, cached);
            return;
        }

        if (expiresAt > DateTimeOffset.UtcNow + RefreshWindow)
        {
            return; // still fresh
        }

        if (!CanRefresh || string.IsNullOrEmpty(refreshToken))
        {
            if (expiresAt <= DateTimeOffset.UtcNow)
            {
                // Pre-offline_access cookie (or refresh disabled): the session can no
                // longer call the API — end it cleanly instead of limping on 401s.
                context.RejectPrincipal();
            }
            return;
        }

        var refreshed = await RefreshAsync(refreshToken, context.HttpContext.RequestAborted);
        if (refreshed is null)
        {
            if (expiresAt <= DateTimeOffset.UtcNow)
            {
                context.RejectPrincipal();
            }
            return;
        }

        if (sub is not null)
        {
            _cache[sub] = refreshed;
        }
        WriteTokens(context, refreshed);
    }

    private static void WriteTokens(CookieValidatePrincipalContext context, CachedTokens tokens)
    {
        context.Properties.UpdateTokenValue("access_token", tokens.AccessToken);
        context.Properties.UpdateTokenValue("refresh_token", tokens.RefreshToken);
        context.Properties.UpdateTokenValue("expires_at", tokens.ExpiresAt.ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }

    // ------------------------------------------------------------- refresh core --

    private async Task<CachedTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(Auth0TokenRefresher));
            var response = await client.PostAsync($"https://{_domain}/oauth/token", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = _clientId!,
                    ["client_secret"] = _clientSecret!,
                    ["refresh_token"] = refreshToken,
                }), ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Auth0 token refresh failed: HTTP {Status} {Body}",
                    (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return null;
            }

            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = payload.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            // Rotation is off, so Auth0 usually omits refresh_token; keep the current one.
            var newRefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : refreshToken;

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            _logger.LogInformation("Auth0 access token refreshed; new expiry {ExpiresAt:o}", expiresAt);
            return new CachedTokens(accessToken, expiresAt, newRefreshToken);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auth0 token refresh failed");
            return null;
        }
    }

    private static async Task<CachedTokens?> ReadStoredTokensAsync(HttpContext httpContext)
    {
        var accessToken = await httpContext.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        var refreshToken = await httpContext.GetTokenAsync("refresh_token") ?? string.Empty;
        var expiresAtRaw = await httpContext.GetTokenAsync("expires_at");
        var expiresAt = expiresAtRaw is not null
            && DateTimeOffset.TryParse(expiresAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTimeOffset.MaxValue; // no expiry recorded — treat as fresh

        return new CachedTokens(accessToken, expiresAt, refreshToken);
    }
}
