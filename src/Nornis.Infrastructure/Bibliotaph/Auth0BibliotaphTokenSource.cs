using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Bibliotaph;

/// <summary>
/// Access tokens for the Bibliotaph API from Auth0's refresh-token grant, as the
/// "Nornis → Bibliotaph" application. A token is reused until a minute before it expires, and a
/// burst of calls on one connection waits for a single refresh. Refresh-token rotation is off
/// for this application, so the API and the Worker can each refresh from the same stored token
/// without revoking each other (design.md, second caller).
///
/// One per process. The cache holds one entry per connection used, which is one per connected GM.
/// </summary>
public sealed class Auth0BibliotaphTokenSource : IBibliotaphTokenSource
{
    public const string HttpClientName = "bibliotaph-auth0";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBibliotaphTokenProtector _protector;
    private readonly BibliotaphOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<Auth0BibliotaphTokenSource> _logger;
    private readonly ConcurrentDictionary<Guid, CachedToken> _tokens = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public Auth0BibliotaphTokenSource(
        IHttpClientFactory httpClientFactory,
        IBibliotaphTokenProtector protector,
        IOptions<BibliotaphOptions> options,
        TimeProvider time,
        ILogger<Auth0BibliotaphTokenSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _protector = protector;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(BibliotaphConnection connection, CancellationToken ct)
    {
        // Keyed on the stored grant as well as the connection, so a reconnect never serves a
        // token minted from the grant it replaced.
        var grantKey = GrantKey(connection.ProtectedRefreshToken);
        if (TryCached(connection.Id, grantKey) is { } cached)
        {
            return cached;
        }

        var gate = _gates.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (TryCached(connection.Id, grantKey) is { } refreshedMeanwhile)
            {
                return refreshedMeanwhile;
            }

            var refreshToken = _protector.Unprotect(connection.ProtectedRefreshToken);
            if (refreshToken is null)
            {
                _logger.LogWarning(
                    "Bibliotaph grant unreadable (lost key ring or bad ciphertext). ConnectionId={ConnectionId}",
                    connection.Id);
                return null;
            }

            var issued = await RefreshAsync(refreshToken, ct);
            if (issued is null)
            {
                _logger.LogWarning("Auth0 refused the Bibliotaph grant. ConnectionId={ConnectionId}", connection.Id);
                return null;
            }

            _tokens[connection.Id] = new CachedToken(grantKey, issued.AccessToken,
                _time.GetUtcNow().AddSeconds(issued.ExpiresIn));
            return issued.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Forget(Guid connectionId) => _tokens.TryRemove(connectionId, out _);

    private string? TryCached(Guid connectionId, string grantKey) =>
        _tokens.TryGetValue(connectionId, out var token)
        && token.GrantKey == grantKey
        && token.ExpiresAt - RefreshMargin > _time.GetUtcNow()
            ? token.AccessToken
            : null;

    private async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TokenTimeoutSeconds));

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId!,
            ["client_secret"] = _options.ClientSecret!,
            ["refresh_token"] = refreshToken,
        });

        using var response = await client.PostAsync(TokenEndpoint(_options), body, timeout.Token);

        // invalid_grant is a 400 (or 403 for a revoked one): the grant is dead and only the GM
        // reconnecting fixes it. Anything else is Auth0 being unwell, which throws and reads as
        // unreachable, so a connection is never marked for a blip.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(timeout.Token);
        return token is { AccessToken.Length: > 0, ExpiresIn: > 0 }
            ? token
            : throw new HttpRequestException("Auth0 answered the refresh without an access token.");
    }

    public static Uri TokenEndpoint(BibliotaphOptions options) => new($"https://{options.Auth0Domain}/oauth/token");

    private static string GrantKey(string protectedRefreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protectedRefreshToken)));

    private sealed record CachedToken(string GrantKey, string AccessToken, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
