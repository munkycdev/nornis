using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Bibliotaph;

/// <summary>Delivering the outbox (feature 31, Requirement 2): batches, backoff, giving up, the purge.</summary>
[TestFixture]
public class BibliotaphOutboxDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private IBibliotaphOutboxRepository _outbox = null!;
    private ILibraryReadingRecordRepository _readingRecords = null!;
    private IBibliotaphClient _client = null!;
    private BibliotaphOptions _options = null!;
    private BibliotaphOutboxDelivery _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _outbox = Substitute.For<IBibliotaphOutboxRepository>();
        _readingRecords = Substitute.For<ILibraryReadingRecordRepository>();
        _client = Substitute.For<IBibliotaphClient>();
        _options = new BibliotaphOptions();
        _sut = new BibliotaphOutboxDelivery(
            _outbox, _readingRecords, _client, Options.Create(_options), new FixedClock(), NullLogger<BibliotaphOutboxDelivery>.Instance);
    }

    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 4)]
    [TestCase(6, 32)]
    [TestCase(7, 60)]
    [TestCase(40, 60)]
    public void Backoff_doubles_from_a_minute_to_at_most_an_hour(int attempts, int minutes)
    {
        Assert.That(BibliotaphOutboxDelivery.Backoff(attempts), Is.EqualTo(TimeSpan.FromMinutes(minutes)));
    }

    [Test]
    public async Task Each_users_events_go_through_their_own_connection_oldest_first_in_batches_of_100()
    {
        var dave = Guid.NewGuid();
        var other = Guid.NewGuid();
        var daves = Enumerable.Range(0, 250).Select(i => Event(dave, Now.AddMinutes(-i))).ToList();
        var others = new[] { Event(other, Now) };
        Due([.. daves, .. others]);
        var sent = new List<(Guid User, IReadOnlyList<BibliotaphWorldEventMessage> Batch)>();
        _client.SendWorldEventsAsync(default, default!, default).ReturnsForAnyArgs(ci =>
        {
            sent.Add((ci.ArgAt<Guid>(0), ci.ArgAt<IReadOnlyList<BibliotaphWorldEventMessage>>(1).ToList()));
            return BibliotaphResult<BibliotaphAccepted>.Success(default);
        });

        await _sut.RunOnceAsync(CancellationToken.None);

        Assert.That(sent.Where(s => s.User == dave).Select(s => s.Batch.Count), Is.EqualTo([100, 100, 50]));
        Assert.That(sent.Where(s => s.User == other).Select(s => s.Batch.Count), Is.EqualTo([1]));
        var daveOrder = sent.Where(s => s.User == dave).SelectMany(s => s.Batch).Select(m => m.OccurredAt).ToList();
        Assert.That(daveOrder, Is.Ordered);
        await _outbox.Received(4).MarkDeliveredAsync(Arg.Any<IReadOnlyList<Guid>>(), Now, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task A_refused_grant_keeps_the_events_waiting_with_backoff()
    {
        var e = Event(Guid.NewGuid(), Now.AddHours(-1), attempts: 2);
        Due([e]);
        Answer(BibliotaphFailure.NotConnected);

        await _sut.RunOnceAsync(CancellationToken.None);

        await _outbox.Received(1).MarkAttemptFailedAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == e.Id), Now + TimeSpan.FromMinutes(4), "NotConnected", Arg.Any<CancellationToken>());
        await _outbox.DidNotReceiveWithAnyArgs().MarkFailedAsync(default!, default, default);
    }

    [Test]
    public async Task No_connection_fails_the_events_at_once()
    {
        var e = Event(Guid.NewGuid(), Now);
        Due([e]);
        Answer(BibliotaphFailure.NoConnection);

        await _sut.RunOnceAsync(CancellationToken.None);

        await _outbox.Received(1).MarkFailedAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == e.Id), Now, Arg.Any<CancellationToken>());
        await _outbox.DidNotReceiveWithAnyArgs().MarkAttemptFailedAsync(default!, default, default!, default);
    }

    [Test]
    public async Task An_event_a_week_old_is_given_up_and_a_younger_one_waits()
    {
        var user = Guid.NewGuid();
        var old = Event(user, Now.AddDays(-7), attempts: 30);
        var young = Event(user, Now.AddDays(-6), attempts: 30);
        Due([old, young]);
        Answer(BibliotaphFailure.Unreachable);

        await _sut.RunOnceAsync(CancellationToken.None);

        await _outbox.Received(1).MarkFailedAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == old.Id), Now, Arg.Any<CancellationToken>());
        await _outbox.Received(1).MarkAttemptFailedAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == young.Id), Now + TimeSpan.FromHours(1), "Unreachable", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Each_pass_purges_what_settled_over_a_week_ago_and_opens_over_90_days_old()
    {
        Due([]);

        await _sut.RunOnceAsync(CancellationToken.None);

        await _outbox.Received(1).PurgeSettledAsync(Now.AddDays(-7), Arg.Any<CancellationToken>());
        await _readingRecords.Received(1).PurgeOlderThanAsync(
            Now.AddDays(-90), BibliotaphOutboxDelivery.ReadingRecordsPurgedPerPass, Arg.Any<CancellationToken>());
    }

    private void Due(IReadOnlyList<BibliotaphWorldEvent> events) =>
        _outbox.ListDueAsync(Now, BibliotaphOutboxDelivery.RowsPerPass, Arg.Any<CancellationToken>()).Returns(events);

    private void Answer(BibliotaphFailure failure) =>
        _client.SendWorldEventsAsync(default, default!, default)
            .ReturnsForAnyArgs(BibliotaphResult<BibliotaphAccepted>.Fail(failure));

    private static BibliotaphWorldEvent Event(Guid userId, DateTimeOffset occurredAt, int attempts = 0) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        WorldId = Guid.NewGuid(),
        Kind = BibliotaphWorldEventKind.Renamed,
        WorldName = "Gribbits",
        OccurredAt = occurredAt,
        NextAttemptAt = Now,
        Attempts = attempts,
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
