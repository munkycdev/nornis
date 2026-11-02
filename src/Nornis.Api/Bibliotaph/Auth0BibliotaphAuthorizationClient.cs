using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;

namespace Nornis.Api.Bibliotaph;

/// <summary>
/// The code exchange and revocation for the "Nornis → Bibliotaph" Auth0 application. The ID
/// token is validated against the tenant's signing keys (issuer, audience = this application,
/// lifetime) before its subject is trusted.
/// </summary>
public sealed class Auth0BibliotaphAuthorizationClient : IBibliotaphAuthorizationClient
{
    public const string HttpClientName = "bibliotaph-auth0-code";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BibliotaphOptions _options;
    private readonly ILogger<Auth0BibliotaphAuthorizationClient> _logger;
    private readonly Lazy<ConfigurationManager<OpenIdConnectConfiguration>> _metadata;

    public Auth0BibliotaphAuthorizationClient(
        IHttpClientFactory httpClientFactory,
        IOptions<BibliotaphOptions> options,
        ILogger<Auth0BibliotaphAuthorizationClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _metadata = new Lazy<ConfigurationManager<OpenIdConnectConfiguration>>(() =>
            new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{Authority}.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(_httpClientFactory.CreateClient(HttpClientName))));
    }

    private string Authority => $"https://{_options.Auth0Domain}/";

    public async Task<BibliotaphGrant?> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId!,
            ["client_secret"] = _options.ClientSecret!,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = _options.CallbackUrl,
        });

        using var response = await client.PostAsync($"{Authority}oauth/token", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Auth0 refused the Bibliotaph code exchange. Status={Status}", (int)response.StatusCode);
            return null;
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (tokens is not { RefreshToken.Length: > 0, IdToken.Length: > 0 })
        {
            _logger.LogWarning("Auth0 answered the Bibliotaph code exchange without a refresh token or ID token; is offline access allowed on the Bibliotaph API?");
            return null;
        }

        var subject = await ValidateIdTokenAsync(tokens.IdToken, ct);
        return subject is null ? null : new BibliotaphGrant(tokens.RefreshToken, subject);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var body = JsonContent.Create(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId!,
                ["client_secret"] = _options.ClientSecret!,
                ["token"] = refreshToken,
            });
            using var response = await client.PostAsync($"{Authority}oauth/revoke", body, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Auth0 refused to revoke a Bibliotaph grant. Status={Status}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Revoking a Bibliotaph grant failed; it is forgotten regardless.");
        }
    }

    private async Task<string?> ValidateIdTokenAsync(string idToken, CancellationToken ct)
    {
        var metadata = await _metadata.Value.GetConfigurationAsync(ct);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = Authority,
            ValidAudience = _options.ClientId,
            IssuerSigningKeys = metadata.SigningKeys,
            ValidateLifetime = true,
        });

        if (!result.IsValid)
        {
            _logger.LogWarning(result.Exception, "A Bibliotaph ID token did not validate.");
            return null;
        }

        return result.Claims.TryGetValue("sub", out var sub) ? sub as string : null;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken);
}
