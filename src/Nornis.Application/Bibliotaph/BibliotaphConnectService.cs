using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Bibliotaph;

public interface IBibliotaphConnectService
{
    Task<BibliotaphConnectionView> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>The Bibliotaph address that starts connecting. Its return address carries the protected state.</summary>
    Task<AppResult<string>> BeginAsync(Guid userId, CancellationToken ct);

    /// <summary>The Auth0 address that asks for the grant, once Bibliotaph sent the browser back.</summary>
    AppResult<string> AuthorizeUrl(Guid userId, string state);

    /// <summary>Auth0's callback. Always answers with where to send the browser.</summary>
    Task<string> CompleteAsync(string? code, string? state, string? error, CancellationToken ct);

    Task<AppResult> DisconnectAsync(Guid userId, CancellationToken ct);
}

/// <summary>What Profile › Bibliotaph shows. <see cref="Status"/> is none, connected or needs_reconnect.</summary>
public sealed record BibliotaphConnectionView(
    bool Configured, string Status, DateTimeOffset? ConnectedAt, DateTimeOffset? LastUsedAt);

/// <summary>
/// Connecting a GM's Bibliotaph (feature 31, Requirement 1). The browser goes to Bibliotaph's
/// <c>/connect/nornis</c>, back to Nornis, to Auth0 for the grant, and to the API's callback,
/// which keeps the refresh token and asks Bibliotaph to make the GM's world collections.
///
/// The state that ties the steps together is protected with the Bibliotaph ring and carries
/// the user, the PKCE verifier and an expiry, so no table is needed. What binds the grant to
/// the right person is the ID token's subject, which must be one of the sign-ins of the user who
/// began.
/// </summary>
public sealed class BibliotaphConnectService : IBibliotaphConnectService
{
    private readonly IBibliotaphConnectionRepository _connections;
    private readonly IBibliotaphOutboxRepository _outbox;
    private readonly IUserRepository _users;
    private readonly IBibliotaphTokenProtector _protector;
    private readonly IBibliotaphTokenSource _tokens;
    private readonly IBibliotaphAuthorizationClient _auth0;
    private readonly BibliotaphOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BibliotaphConnectService> _logger;

    public BibliotaphConnectService(
        IBibliotaphConnectionRepository connections,
        IBibliotaphOutboxRepository outbox,
        IUserRepository users,
        IBibliotaphTokenProtector protector,
        IBibliotaphTokenSource tokens,
        IBibliotaphAuthorizationClient auth0,
        IOptions<BibliotaphOptions> options,
        TimeProvider time,
        ILogger<BibliotaphConnectService> logger)
    {
        _connections = connections;
        _outbox = outbox;
        _users = users;
        _protector = protector;
        _tokens = tokens;
        _auth0 = auth0;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    private bool Configured => _options.IsConfigured && !string.IsNullOrWhiteSpace(_options.WebOrigin);

    private string WebOrigin => _options.WebOrigin!.TrimEnd('/');

    public async Task<BibliotaphConnectionView> GetAsync(Guid userId, CancellationToken ct)
    {
        if (!Configured)
        {
            return new BibliotaphConnectionView(false, "none", null, null);
        }

        var connection = await _connections.GetByUserIdAsync(userId, ct);
        return connection is null
            ? new BibliotaphConnectionView(true, "none", null, null)
            : new BibliotaphConnectionView(true,
                connection.Status == BibliotaphConnectionStatus.Connected ? "connected" : "needs_reconnect",
                connection.ConnectedAt, connection.LastUsedAt);
    }

    public Task<AppResult<string>> BeginAsync(Guid userId, CancellationToken ct)
    {
        if (!Configured)
        {
            return Task.FromResult(AppResult<string>.Fail(NotConfigured));
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = _time.GetUtcNow().AddMinutes(_options.ConnectStateMinutes).ToUnixTimeSeconds();
        var state = _protector.ProtectState(JsonSerializer.Serialize(new ConnectState(userId, verifier, expires)));

        // The state rides in the path, so Bibliotaph can append ?bibliotaph=… to the address as is.
        var returnAddress = $"{WebOrigin}/profile/bibliotaph/{Uri.EscapeDataString(state)}";
        var url = $"{_options.BaseUrl!.TrimEnd('/')}/connect/nornis?return={Uri.EscapeDataString(returnAddress)}";
        return Task.FromResult(AppResult<string>.Success(url));
    }

    public AppResult<string> AuthorizeUrl(Guid userId, string state)
    {
        if (!Configured)
        {
            return AppResult<string>.Fail(NotConfigured);
        }

        var parsed = ReadState(state);
        if (parsed is null || parsed.UserId != userId)
        {
            return AppResult<string>.Fail(new AppError(400, "connect_expired",
                "That connection attempt has expired. Press Connect Bibliotaph again."));
        }

        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(parsed.Verifier)));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId!,
            ["redirect_uri"] = _options.CallbackUrl,
            ["scope"] = BibliotaphOptions.Scopes,
            ["audience"] = _options.Audience,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        var url = $"https://{_options.Auth0Domain}/authorize?" +
                  string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return AppResult<string>.Success(url);
    }

    public async Task<string> CompleteAsync(string? code, string? state, string? error, CancellationToken ct)
    {
        if (!Configured)
        {
            return "/";
        }

        var parsed = state is null ? null : ReadState(state);
        if (parsed is null)
        {
            _logger.LogWarning("Bibliotaph callback with an unreadable or expired state.");
            return Back("failed");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            // The GM declined consent, or Auth0 refused the request; its error code is safe to log.
            _logger.LogInformation("Bibliotaph connect ended at Auth0. UserId={UserId}, Error={Error}", parsed.UserId, error);
            return Back("refused");
        }

        var user = await _users.GetByIdAsync(parsed.UserId, ct);
        if (user is null)
        {
            return Back("failed");
        }

        BibliotaphGrant? grant;
        try
        {
            grant = await _auth0.ExchangeCodeAsync(code, parsed.Verifier, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Bibliotaph code exchange failed. UserId={UserId}", user.Id);
            return Back("failed");
        }

        if (grant is null)
        {
            return Back("failed");
        }

        // Any of the user's sign-ins may be the one used at Bibliotaph (feature 33); a subject
        // that is nobody's, or another user's, is refused.
        var grantOwner = string.IsNullOrEmpty(grant.Subject) ? null : await _users.GetBySubjectAsync(grant.Subject, ct);
        if (grantOwner?.Id != user.Id)
        {
            // Someone else's Bibliotaph sign-in completed this user's flow. Never keep it.
            _logger.LogWarning("Bibliotaph grant refused: its subject is not the user who began. UserId={UserId}", user.Id);
            await _auth0.RevokeAsync(grant.RefreshToken, ct);
            return Back("refused");
        }

        var previous = await _connections.GetByUserIdAsync(user.Id, ct);
        var now = _time.GetUtcNow();
        var connection = await _connections.UpsertAsync(user.Id, _protector.Protect(grant.RefreshToken), now, ct);
        _tokens.Forget(connection.Id);
        if (previous is not null && _protector.Unprotect(previous.ProtectedRefreshToken) is { } replaced)
        {
            await _auth0.RevokeAsync(replaced, ct);
        }

        var queued = await _outbox.QueueCreatedForWorldsOfAsync(user.Id, now, ct);
        _logger.LogInformation("Bibliotaph connected. UserId={UserId}, WorldsQueued={Worlds}", user.Id, queued);
        return Back("connected");
    }

    public async Task<AppResult> DisconnectAsync(Guid userId, CancellationToken ct)
    {
        var connection = await _connections.GetByUserIdAsync(userId, ct);
        if (connection is null)
        {
            return AppResult.Success();
        }

        if (_protector.Unprotect(connection.ProtectedRefreshToken) is { } refreshToken)
        {
            await _auth0.RevokeAsync(refreshToken, ct);
        }

        await _outbox.DeleteUndeliveredAsync(userId, ct);
        await _connections.DeleteAsync(connection.Id, ct);
        _tokens.Forget(connection.Id);
        _logger.LogInformation("Bibliotaph disconnected. UserId={UserId}", userId);
        return AppResult.Success();
    }

    private string Back(string outcome) => $"{WebOrigin}/profile?bibliotaph={outcome}";

    private ConnectState? ReadState(string state)
    {
        var json = _protector.UnprotectState(state);
        if (json is null)
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ConnectState>(json);
            return parsed is not null && parsed.ExpiresAt > _time.GetUtcNow().ToUnixTimeSeconds() ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static readonly AppError NotConfigured =
        new(404, "bibliotaph_not_configured", "Bibliotaph isn't set up on this Nornis.");

    private sealed record ConnectState(Guid UserId, string Verifier, long ExpiresAt);
}
