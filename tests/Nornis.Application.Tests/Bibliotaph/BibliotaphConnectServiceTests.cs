using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Bibliotaph;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Bibliotaph;

/// <summary>Connecting (feature 31, Requirement 1): the state, the subject check, and what is kept.</summary>
[TestFixture]
public class BibliotaphConnectServiceTests
{
    private const string WebOrigin = "https://nornis.example";

    private readonly User _dave = new() { Id = Guid.NewGuid(), Auth0SubjectId = "auth0|dave" };

    private IBibliotaphConnectionRepository _connections = null!;
    private IBibliotaphOutboxRepository _outbox = null!;
    private IUserRepository _users = null!;
    private IBibliotaphTokenSource _tokens = null!;
    private IBibliotaphAuthorizationClient _auth0 = null!;
    private BibliotaphTokenProtector _protector = null!;
    private BibliotaphOptions _options = null!;
    private Clock _time = null!;

    [SetUp]
    public void SetUp()
    {
        _connections = Substitute.For<IBibliotaphConnectionRepository>();
        _connections.UpsertAsync(default, default!, default, default)
            .ReturnsForAnyArgs(ci => new BibliotaphConnection { Id = Guid.NewGuid(), UserId = ci.ArgAt<Guid>(0) });
        _outbox = Substitute.For<IBibliotaphOutboxRepository>();
        _users = Substitute.For<IUserRepository>();
        _users.GetByIdAsync(_dave.Id, Arg.Any<CancellationToken>()).Returns(_dave);
        _tokens = Substitute.For<IBibliotaphTokenSource>();
        _auth0 = Substitute.For<IBibliotaphAuthorizationClient>();
        _protector = BibliotaphTokenProtector.Create(null);
        _time = new Clock();
        _options = new BibliotaphOptions
        {
            BaseUrl = "https://bibliotaph.example",
            Auth0Domain = "auth.example",
            ClientId = "nornis-to-bibliotaph",
            ClientSecret = "secret",
            WebOrigin = WebOrigin,
            KeyBlobUri = "https://keys.example/k.xml",
        };
    }

    [Test]
    public async Task Unconfigured_shows_nothing_and_begins_nothing()
    {
        _options.ClientSecret = null;

        var view = await Sut().GetAsync(_dave.Id, CancellationToken.None);
        var begin = await Sut().BeginAsync(_dave.Id, CancellationToken.None);

        Assert.That(view.Configured, Is.False);
        Assert.That(begin.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Begin_sends_the_browser_to_Bibliotaph_with_a_return_address_on_Nornis()
    {
        var url = (await Sut().BeginAsync(_dave.Id, CancellationToken.None)).Value!;

        Assert.That(url, Does.StartWith("https://bibliotaph.example/connect/nornis?return="));
        var returnAddress = Uri.UnescapeDataString(url[(url.IndexOf("return=", StringComparison.Ordinal) + 7)..]);
        Assert.That(returnAddress, Does.StartWith($"{WebOrigin}/profile/bibliotaph/"));
        Assert.That(returnAddress, Does.Not.Contain("?"), "Bibliotaph appends ?bibliotaph=…, so the address has no query of its own.");
    }

    [Test]
    public async Task Authorize_asks_Auth0_for_the_contracts_scopes_with_PKCE()
    {
        var state = await StateFor(_dave.Id);

        var url = Sut().AuthorizeUrl(_dave.Id, state).Value!;

        var query = Query(url);
        Assert.Multiple(() =>
        {
            Assert.That(url, Does.StartWith("https://auth.example/authorize?"));
            Assert.That(query["scope"], Is.EqualTo("openid books:read tickets:write worlds:write offline_access"));
            Assert.That(query["audience"], Is.EqualTo("https://bibliotaph.online/api"));
            Assert.That(query["redirect_uri"], Is.EqualTo("https://api.nornis.app/bibliotaph/callback"));
            Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
            Assert.That(query["state"], Is.EqualTo(state));
        });
    }

    [Test]
    public async Task Another_users_state_is_refused()
    {
        var state = await StateFor(_dave.Id);

        var result = Sut().AuthorizeUrl(Guid.NewGuid(), state);

        Assert.That(result.Error!.Code, Is.EqualTo("connect_expired"));
    }

    [Test]
    public async Task An_expired_state_is_refused()
    {
        var state = await StateFor(_dave.Id);
        _time.Now = _time.Now.AddMinutes(11);

        Assert.That(Sut().AuthorizeUrl(_dave.Id, state).Error!.Code, Is.EqualTo("connect_expired"));
    }

    [Test]
    public async Task Completing_keeps_the_protected_grant_and_queues_the_worlds()
    {
        var state = await StateFor(_dave.Id);
        var verifier = VerifierSentFor(state);
        _auth0.ExchangeCodeAsync("code-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BibliotaphGrant("refresh-1", _dave.Auth0SubjectId));

        var redirect = await Sut().CompleteAsync("code-1", state, null, CancellationToken.None);

        Assert.That(redirect, Is.EqualTo($"{WebOrigin}/profile?bibliotaph=connected"));
        await _auth0.Received(1).ExchangeCodeAsync("code-1", Arg.Is<string>(v => Challenge(v) == verifier), Arg.Any<CancellationToken>());
        await _connections.Received(1).UpsertAsync(_dave.Id,
            Arg.Is<string>(p => p != "refresh-1" && _protector.Unprotect(p) == "refresh-1"),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _outbox.Received(1).QueueCreatedForWorldsOfAsync(_dave.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task A_grant_for_someone_else_is_revoked_and_never_kept()
    {
        var state = await StateFor(_dave.Id);
        _auth0.ExchangeCodeAsync(default!, default!, default)
            .ReturnsForAnyArgs(new BibliotaphGrant("refresh-x", "auth0|someone-else"));

        var redirect = await Sut().CompleteAsync("code-1", state, null, CancellationToken.None);

        Assert.That(redirect, Is.EqualTo($"{WebOrigin}/profile?bibliotaph=refused"));
        await _auth0.Received(1).RevokeAsync("refresh-x", Arg.Any<CancellationToken>());
        await _connections.DidNotReceiveWithAnyArgs().UpsertAsync(default, default!, default, default);
        await _outbox.DidNotReceiveWithAnyArgs().QueueCreatedForWorldsOfAsync(default, default, default);
    }

    [Test]
    public async Task Reconnecting_revokes_the_grant_it_replaces()
    {
        _connections.GetByUserIdAsync(_dave.Id, Arg.Any<CancellationToken>())
            .Returns(new BibliotaphConnection { Id = Guid.NewGuid(), UserId = _dave.Id, ProtectedRefreshToken = _protector.Protect("old") });
        _auth0.ExchangeCodeAsync(default!, default!, default)
            .ReturnsForAnyArgs(new BibliotaphGrant("new", _dave.Auth0SubjectId));

        await Sut().CompleteAsync("code-1", await StateFor(_dave.Id), null, CancellationToken.None);

        await _auth0.Received(1).RevokeAsync("old", Arg.Any<CancellationToken>());
        await _auth0.DidNotReceive().RevokeAsync("new", Arg.Any<CancellationToken>());
    }

    [TestCase("access_denied", "code-1", "refused")]
    [TestCase(null, null, "refused")]
    public async Task Auth0_ending_the_flow_keeps_nothing(string? error, string? code, string outcome)
    {
        var redirect = await Sut().CompleteAsync(code, await StateFor(_dave.Id), error, CancellationToken.None);

        Assert.That(redirect, Is.EqualTo($"{WebOrigin}/profile?bibliotaph={outcome}"));
        await _auth0.DidNotReceiveWithAnyArgs().ExchangeCodeAsync(default!, default!, default);
    }

    [Test]
    public async Task A_forged_state_keeps_nothing()
    {
        var redirect = await Sut().CompleteAsync("code-1", "not-a-state", null, CancellationToken.None);

        Assert.That(redirect, Is.EqualTo($"{WebOrigin}/profile?bibliotaph=failed"));
        await _auth0.DidNotReceiveWithAnyArgs().ExchangeCodeAsync(default!, default!, default);
    }

    [Test]
    public async Task A_refused_exchange_keeps_nothing()
    {
        _auth0.ExchangeCodeAsync(default!, default!, default).ReturnsForAnyArgs((BibliotaphGrant?)null);

        var redirect = await Sut().CompleteAsync("code-1", await StateFor(_dave.Id), null, CancellationToken.None);

        Assert.That(redirect, Is.EqualTo($"{WebOrigin}/profile?bibliotaph=failed"));
        await _connections.DidNotReceiveWithAnyArgs().UpsertAsync(default, default!, default, default);
    }

    [Test]
    public async Task Disconnect_revokes_drops_undelivered_events_and_forgets_the_connection()
    {
        var connection = new BibliotaphConnection { Id = Guid.NewGuid(), UserId = _dave.Id, ProtectedRefreshToken = _protector.Protect("grant") };
        _connections.GetByUserIdAsync(_dave.Id, Arg.Any<CancellationToken>()).Returns(connection);

        var result = await Sut().DisconnectAsync(_dave.Id, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        await _auth0.Received(1).RevokeAsync("grant", Arg.Any<CancellationToken>());
        await _outbox.Received(1).DeleteUndeliveredAsync(_dave.Id, Arg.Any<CancellationToken>());
        await _connections.Received(1).DeleteAsync(connection.Id, Arg.Any<CancellationToken>());
        _tokens.Received(1).Forget(connection.Id);
    }

    [Test]
    public async Task The_view_names_the_connections_state()
    {
        _connections.GetByUserIdAsync(_dave.Id, Arg.Any<CancellationToken>())
            .Returns(new BibliotaphConnection { Status = BibliotaphConnectionStatus.NeedsReconnect });

        var view = await Sut().GetAsync(_dave.Id, CancellationToken.None);

        Assert.That(view.Status, Is.EqualTo("needs_reconnect"));
    }

    private async Task<string> StateFor(Guid userId)
    {
        var url = (await Sut().BeginAsync(userId, CancellationToken.None)).Value!;
        var returnAddress = Uri.UnescapeDataString(url[(url.IndexOf("return=", StringComparison.Ordinal) + 7)..]);
        return Uri.UnescapeDataString(returnAddress[(returnAddress.LastIndexOf('/') + 1)..]);
    }

    private string VerifierSentFor(string state) => Query(Sut().AuthorizeUrl(_dave.Id, state).Value!)["code_challenge"];

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Dictionary<string, string> Query(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    private BibliotaphConnectService Sut() =>
        new(_connections, _outbox, _users, _protector, _tokens, _auth0, Options.Create(_options), _time,
            NullLogger<BibliotaphConnectService>.Instance);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
