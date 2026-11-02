using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Infrastructure.Bibliotaph;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Bibliotaph;

[TestFixture]
public class Auth0BibliotaphTokenSourceTests
{
    private readonly BibliotaphOptions _options = new()
    {
        Auth0Domain = "auth.example",
        ClientId = "nornis-to-bibliotaph",
        ClientSecret = "secret",
    };

    private BibliotaphTokenProtector _protector = null!;
    private ManualTime _time = null!;

    [SetUp]
    public void SetUp()
    {
        _protector = BibliotaphTokenProtector.Create(null);
        _time = new ManualTime();
    }

    [Test]
    public async Task A_token_is_reused_until_a_minute_before_it_expires()
    {
        var handler = Issuing();
        var sut = Sut(handler);
        var connection = Connection("refresh-1");

        var first = await sut.GetAccessTokenAsync(connection, CancellationToken.None);
        _time.Now = _time.Now.AddSeconds(3500 - 61);
        var second = await sut.GetAccessTokenAsync(connection, CancellationToken.None);
        _time.Now = _time.Now.AddSeconds(2);
        var third = await sut.GetAccessTokenAsync(connection, CancellationToken.None);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(third, Is.Not.EqualTo(first));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task The_refresh_sends_the_grant_as_the_Nornis_to_Bibliotaph_application()
    {
        var handler = Issuing();

        await Sut(handler).GetAccessTokenAsync(Connection("refresh-1"), CancellationToken.None);

        var (request, body) = handler.Requests.Single();
        Assert.That(request.RequestUri!.ToString(), Is.EqualTo("https://auth.example/oauth/token"));
        Assert.That(body, Does.Contain("grant_type=refresh_token"));
        Assert.That(body, Does.Contain("client_id=nornis-to-bibliotaph"));
        Assert.That(body, Does.Contain("refresh_token=refresh-1"));
    }

    [Test]
    public async Task A_burst_of_calls_waits_for_one_refresh()
    {
        var handler = Issuing(delay: TimeSpan.FromMilliseconds(100));
        var sut = Sut(handler);
        var connection = Connection("refresh-1");

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => sut.GetAccessTokenAsync(connection, CancellationToken.None)));

        Assert.That(tokens.Distinct().Count(), Is.EqualTo(1));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_reconnect_never_serves_a_token_from_the_grant_it_replaced()
    {
        var handler = Issuing();
        var sut = Sut(handler);
        var connection = Connection("refresh-1");

        var before = await sut.GetAccessTokenAsync(connection, CancellationToken.None);
        connection.ProtectedRefreshToken = _protector.Protect("refresh-2");
        var after = await sut.GetAccessTokenAsync(connection, CancellationToken.None);

        Assert.That(after, Is.Not.EqualTo(before));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task Invalid_grant_is_a_refusal(HttpStatusCode status)
    {
        var handler = new StubHandler(status, """{"error":"invalid_grant"}""");

        var token = await Sut(handler).GetAccessTokenAsync(Connection("refresh-1"), CancellationToken.None);

        Assert.That(token, Is.Null);
    }

    [Test]
    public void Auth0_being_unwell_throws_rather_than_marking_the_connection()
    {
        var handler = new StubHandler(HttpStatusCode.BadGateway);

        Assert.That(async () => await Sut(handler).GetAccessTokenAsync(Connection("refresh-1"), CancellationToken.None),
            Throws.InstanceOf<HttpRequestException>());
    }

    [Test]
    public async Task An_unreadable_grant_is_a_refusal_and_calls_nobody()
    {
        var handler = Issuing();
        var connection = Connection("refresh-1");
        connection.ProtectedRefreshToken = BibliotaphTokenProtector.Create(null).Protect("from another ring");

        var token = await Sut(handler).GetAccessTokenAsync(connection, CancellationToken.None);

        Assert.That(token, Is.Null);
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task Forget_drops_the_cached_token()
    {
        var handler = Issuing();
        var sut = Sut(handler);
        var connection = Connection("refresh-1");

        await sut.GetAccessTokenAsync(connection, CancellationToken.None);
        sut.Forget(connection.Id);
        await sut.GetAccessTokenAsync(connection, CancellationToken.None);

        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    private static StubHandler Issuing(TimeSpan? delay = null)
    {
        var issued = 0;
        return new StubHandler(async (_, ct) =>
        {
            if (delay is { } d)
            {
                await Task.Delay(d, ct);
            }

            var n = Interlocked.Increment(ref issued);
            return StubHandler.Respond(HttpStatusCode.OK, $$"""{"access_token":"access-{{n}}","expires_in":3500,"token_type":"Bearer"}""");
        });
    }

    private BibliotaphConnection Connection(string refreshToken) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        ProtectedRefreshToken = _protector.Protect(refreshToken),
    };

    private Auth0BibliotaphTokenSource Sut(StubHandler handler) =>
        new(new SingleClientFactory(handler), _protector, Options.Create(_options), _time,
            NullLogger<Auth0BibliotaphTokenSource>.Instance);
}
