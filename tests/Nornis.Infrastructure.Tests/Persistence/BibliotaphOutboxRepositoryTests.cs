using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

[TestFixture]
public class BibliotaphOutboxRepositoryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private BibliotaphOutboxRepository _sut = null!;
    private User _dave = null!;
    private User _other = null!;

    [SetUp]
    public async Task SetUp()
    {
        _sut = new BibliotaphOutboxRepository(Context);

        // The fixture shares one database across its tests; the outbox is global, so start empty.
        Context.BibliotaphWorldEvents.RemoveRange(await Context.BibliotaphWorldEvents.ToListAsync());
        await Context.SaveChangesAsync();
        _dave = NewUser("dave");
        _other = NewUser("other");
        Context.Users.AddRange(_dave, _other);
        Context.Worlds.AddRange(
            NewWorld("Gribbits Detective Agency", _dave.Id),
            NewWorld("Black Harbor", _dave.Id),
            NewWorld("Tutorial", _dave.Id, demo: true),
            NewWorld("Template", _dave.Id, template: true),
            NewWorld("Someone else's", _other.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    [Test]
    public async Task Backfill_queues_created_for_each_world_the_user_made_and_never_a_demo_or_template()
    {
        var queued = await _sut.QueueCreatedForWorldsOfAsync(_dave.Id, Now);

        var events = await Context.BibliotaphWorldEvents.AsNoTracking().ToListAsync();
        Assert.That(queued, Is.EqualTo(2));
        Assert.That(events.Select(e => e.WorldName), Is.EquivalentTo(["Gribbits Detective Agency", "Black Harbor"]));
        Assert.That(events, Has.All.Matches<BibliotaphWorldEvent>(e =>
            e.Kind == BibliotaphWorldEventKind.Created && e.UserId == _dave.Id && e.NextAttemptAt == Now));
    }

    [Test]
    public async Task Due_events_are_the_undelivered_unfailed_ones_whose_time_has_come_oldest_first()
    {
        var late = Event(Now.AddMinutes(-1), next: Now);
        var early = Event(Now.AddMinutes(-5), next: Now.AddMinutes(-1));
        var notYet = Event(Now.AddMinutes(-9), next: Now.AddMinutes(1));
        var delivered = Event(Now.AddMinutes(-9), next: Now, delivered: Now);
        var failed = Event(Now.AddMinutes(-9), next: Now, failed: Now);
        Context.BibliotaphWorldEvents.AddRange(late, early, notYet, delivered, failed);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var due = await _sut.ListDueAsync(Now, 10);

        Assert.That(due.Select(e => e.Id).ToArray(), Is.EqualTo([early.Id, late.Id]));
        Assert.That((await _sut.ListDueAsync(Now, 1)).Single().Id, Is.EqualTo(early.Id));
    }

    [Test]
    public async Task A_failed_attempt_counts_and_waits()
    {
        var e = Event(Now, next: Now);
        Context.BibliotaphWorldEvents.Add(e);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        await _sut.MarkAttemptFailedAsync([e.Id], Now.AddMinutes(2), "503 opening");

        var stored = await Context.BibliotaphWorldEvents.AsNoTracking().SingleAsync();
        Assert.That(stored.Attempts, Is.EqualTo(1));
        Assert.That(stored.NextAttemptAt, Is.EqualTo(Now.AddMinutes(2)));
        Assert.That(stored.LastError, Is.EqualTo("503 opening"));
    }

    [Test]
    public async Task Disconnect_drops_only_the_users_undelivered_events()
    {
        var pending = Event(Now, next: Now);
        var delivered = Event(Now, next: Now, delivered: Now);
        var othersPending = Event(Now, next: Now, user: _other.Id);
        Context.BibliotaphWorldEvents.AddRange(pending, delivered, othersPending);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        await _sut.DeleteUndeliveredAsync(_dave.Id);

        var left = await Context.BibliotaphWorldEvents.AsNoTracking().Select(x => x.Id).ToListAsync();
        Assert.That(left, Is.EquivalentTo([delivered.Id, othersPending.Id]));
    }

    [Test]
    public async Task Purge_removes_only_settled_events_older_than_the_cutoff()
    {
        var oldDelivered = Event(Now, next: Now, delivered: Now.AddDays(-8));
        var oldFailed = Event(Now, next: Now, failed: Now.AddDays(-8));
        var recentDelivered = Event(Now, next: Now, delivered: Now.AddDays(-1));
        var pending = Event(Now.AddDays(-30), next: Now);
        Context.BibliotaphWorldEvents.AddRange(oldDelivered, oldFailed, recentDelivered, pending);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var purged = await _sut.PurgeSettledAsync(Now.AddDays(-7));

        var left = await Context.BibliotaphWorldEvents.AsNoTracking().Select(x => x.Id).ToListAsync();
        Assert.That(purged, Is.EqualTo(2));
        Assert.That(left, Is.EquivalentTo([recentDelivered.Id, pending.Id]));
    }

    private BibliotaphWorldEvent Event(DateTimeOffset occurred, DateTimeOffset next,
        DateTimeOffset? delivered = null, DateTimeOffset? failed = null, Guid? user = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = user ?? _dave.Id,
            WorldId = Guid.NewGuid(),
            Kind = BibliotaphWorldEventKind.Renamed,
            WorldName = "Gribbits",
            OccurredAt = occurred,
            NextAttemptAt = next,
            DeliveredAt = delivered,
            FailedAt = failed,
        };

    private static User NewUser(string name) => new()
    {
        Id = Guid.NewGuid(),
        Auth0SubjectId = $"auth0|{name}-{Guid.NewGuid():N}",
        Username = name,
        Email = $"{name}@example.com",
        CreatedAt = Now,
        UpdatedAt = Now,
        RowVersion = [],
    };

    private static World NewWorld(string name, Guid creator, bool demo = false, bool template = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedAt = Now,
        UpdatedAt = Now,
        CreatedByUserId = creator,
        IsDemo = demo,
        IsTemplate = template,
        RowVersion = [],
    };
}
