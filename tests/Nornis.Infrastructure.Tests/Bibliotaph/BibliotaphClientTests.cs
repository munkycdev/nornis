using System.Net;
using System.Text.Json;
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

namespace Nornis.Infrastructure.Tests.Bibliotaph;

/// <summary>
/// The client is the one place a Bibliotaph answer becomes Nornis state (design.md, check 1), so
/// these drive it against a stand-in Bibliotaph and look at both the result and the connection.
/// </summary>
[TestFixture]
public class BibliotaphClientTests
{
    private static readonly Guid GmId = Guid.NewGuid();
    private static readonly Guid WorldId = Guid.NewGuid();

    private IBibliotaphConnectionRepository _connections = null!;
    private IBibliotaphTokenSource _tokens = null!;
    private BibliotaphConnection _connection = null!;
    private BibliotaphOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new BibliotaphConnection
        {
            Id = Guid.NewGuid(),
            UserId = GmId,
            ProtectedRefreshToken = "protected",
            Status = BibliotaphConnectionStatus.Connected,
        };
        _connections = Substitute.For<IBibliotaphConnectionRepository>();
        _connections.GetByUserIdAsync(GmId, Arg.Any<CancellationToken>()).Returns(_connection);
        _tokens = Substitute.For<IBibliotaphTokenSource>();
        _tokens.GetAccessTokenAsync(_connection, Arg.Any<CancellationToken>()).Returns("access-token");
        _options = new BibliotaphOptions { BaseUrl = "https://bibliotaph.example", PassagesTimeoutSeconds = 1 };
    }

    [Test]
    public async Task Books_are_read_with_the_bearer_and_no_write_header()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"books":[{"entryId":"e1","documentId":"d1","title":"Player's Guide","pageCount":320,"coverName":"c1","nornisFileId":null}]}""");

        var result = await Sut(handler).ListBooksAsync(GmId, WorldId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Books.Single().Title, Is.EqualTo("Player's Guide"));
        var request = handler.Requests.Single().Request;
        Assert.That(request.RequestUri!.ToString(), Is.EqualTo($"https://bibliotaph.example/api/nornis/v1/worlds/{WorldId}/books"));
        Assert.That(request.Headers.Authorization!.ToString(), Is.EqualTo("Bearer access-token"));
        Assert.That(request.Headers.Contains("X-Bibliotaph"), Is.False);
        await _connections.Received(1).TouchLastUsedAsync(_connection.Id, Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task A_write_carries_the_header_and_leaves_absent_entry_ids_out()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"covered":true,"meaningUsed":false,"pages":[]}""");

        await Sut(handler).FindPassagesAsync(GmId, WorldId,
            new BibliotaphPassagesRequest("How does grappling work?", 10, null, BibliotaphPassageModes.Ask), CancellationToken.None);

        var (request, body) = handler.Requests.Single();
        Assert.That(request.Headers.GetValues("X-Bibliotaph").Single(), Is.EqualTo("nornis"));
        using var json = JsonDocument.Parse(body!);
        Assert.That(json.RootElement.TryGetProperty("entryIds", out _), Is.False,
            "Absent entry ids mean the whole collection; a player's call always sends them.");
        Assert.That(json.RootElement.GetProperty("mode").GetString(), Is.EqualTo("ask"));
    }

    [Test]
    public async Task No_connection_makes_no_call()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        var result = await Sut(handler).ListBooksAsync(Guid.NewGuid(), WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.EqualTo(BibliotaphFailure.NoConnection));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task A_connection_needing_reconnect_makes_no_call()
    {
        _connection.Status = BibliotaphConnectionStatus.NeedsReconnect;
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        var result = await Sut(handler).ListBooksAsync(GmId, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.EqualTo(BibliotaphFailure.NotConnected));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task A_refused_refresh_marks_the_connection()
    {
        _tokens.GetAccessTokenAsync(_connection, Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await Sut(new StubHandler(HttpStatusCode.OK, "{}")).ListBooksAsync(GmId, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.EqualTo(BibliotaphFailure.NotConnected));
        await _connections.Received(1).MarkNeedsReconnectAsync(_connection.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Not_connected_marks_the_connection_and_forgets_the_token()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"code":"not_connected"}""");

        var result = await Sut(handler).ListBooksAsync(GmId, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.EqualTo(BibliotaphFailure.NotConnected));
        await _connections.Received(1).MarkNeedsReconnectAsync(_connection.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        _tokens.Received(1).Forget(_connection.Id);
    }

    [Test]
    public async Task Kept_from_AI_is_its_own_answer_and_leaves_the_connection_alone()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, """{"code":"kept_from_ai"}""");

        var result = await Sut(handler).GetPagesAsync(GmId, WorldId, "d1", 0, 1, CancellationToken.None);

        Assert.That(result.Failure, Is.EqualTo(BibliotaphFailure.KeptFromAi));
        await _connections.DidNotReceive().MarkNeedsReconnectAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [TestCase(HttpStatusCode.ServiceUnavailable, "opening", BibliotaphFailure.Opening)]
    [TestCase(HttpStatusCode.ServiceUnavailable, null, BibliotaphFailure.Unreachable)]
    [TestCase(HttpStatusCode.InternalServerError, null, BibliotaphFailure.Unreachable)]
    [TestCase(HttpStatusCode.TooManyRequests, null, BibliotaphFailure.Unreachable)]
    [TestCase(HttpStatusCode.NotFound, null, BibliotaphFailure.NotFound)]
    [TestCase(HttpStatusCode.BadRequest, null, BibliotaphFailure.Rejected)]
    [TestCase(HttpStatusCode.Forbidden, null, BibliotaphFailure.NotConnected)]
    [TestCase(HttpStatusCode.Unauthorized, null, BibliotaphFailure.NotConnected)]
    public void Each_refusal_is_classified(HttpStatusCode status, string? code, BibliotaphFailure expected)
    {
        Assert.That(BibliotaphClient.Classify(status, code), Is.EqualTo(expected));
    }

    [Test]
    public async Task A_slow_Bibliotaph_is_unreachable_within_the_budget()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHandler.Respond(HttpStatusCode.OK, "{}");
        });

        var started = DateTimeOffset.UtcNow;
        var result = await Sut(handler).FindPassagesAsync(GmId, WorldId,
            new BibliotaphPassagesRequest("q", 10, ["e1"], BibliotaphPassageModes.Ask), CancellationToken.None);

        Assert.That(result.Failure, Is.EqualTo(BibliotaphFailure.Unreachable));
        Assert.That(DateTimeOffset.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void The_callers_own_cancellation_is_not_swallowed()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHandler.Respond(HttpStatusCode.OK, "{}");
        });
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.That(async () => await Sut(handler).ListBooksAsync(GmId, WorldId, cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task A_cover_over_the_cap_is_refused()
    {
        _options.MaxCoverBytes = 10;
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[11]),
        }));

        var result = await Sut(handler).GetCoverAsync(GmId, "cover name", CancellationToken.None);

        Assert.That(result.Failure, Is.EqualTo(BibliotaphFailure.Unreachable));
        Assert.That(handler.Requests.Single().Request.RequestUri!.AbsolutePath, Is.EqualTo("/api/nornis/v1/covers/cover%20name"));
    }

    [TestCase(0)]
    [TestCase(101)]
    public void A_webhook_batch_must_hold_1_to_100_events(int count)
    {
        var events = Enumerable.Range(0, count)
            .Select(_ => new BibliotaphWorldEventMessage(Guid.NewGuid(), "created", WorldId, "Gribbits", DateTimeOffset.UtcNow))
            .ToList();

        Assert.That(() => Sut(new StubHandler(HttpStatusCode.Accepted)).SendWorldEventsAsync(GmId, events, CancellationToken.None),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public async Task Events_go_out_under_their_wire_names()
    {
        var handler = new StubHandler(HttpStatusCode.Accepted);
        var events = new[] { new BibliotaphWorldEventMessage(Guid.NewGuid(), BibliotaphWireNames.Of(BibliotaphWorldEventKind.FilesChanged), WorldId, "Gribbits", DateTimeOffset.UtcNow) };

        var result = await Sut(handler).SendWorldEventsAsync(GmId, events, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        using var json = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.That(json.RootElement[0].GetProperty("kind").GetString(), Is.EqualTo("files-changed"));
        Assert.That(handler.Requests.Single().Request.RequestUri!.AbsolutePath, Is.EqualTo("/api/nornis/v1/worlds/events"));
    }

    private BibliotaphClient Sut(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://bibliotaph.example/") },
            _connections, _tokens, Options.Create(_options), new ManualTime(), NullLogger<BibliotaphClient>.Instance);
}
