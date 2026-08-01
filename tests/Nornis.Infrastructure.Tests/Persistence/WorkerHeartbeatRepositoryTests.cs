using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The repository's one job is that beating twice overwrites rather than accumulates —
/// a worker beats every minute forever, so an insert-only implementation would grow a
/// table without bound and make freshness a max() query.
/// </summary>
[TestFixture]
public class WorkerHeartbeatRepositoryTests : IntegrationTestBase
{
    private const string WorkerName = "nornis-worker";

    // NUnit shares one fixture instance — and so one in-memory database — across every
    // test here, so rows written by one would otherwise be present for the next.
    [SetUp]
    public async Task SetUp()
    {
        Context.Set<WorkerHeartbeat>().RemoveRange(Context.Set<WorkerHeartbeat>());
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    [Test]
    public async Task GetLastBeat_BeforeAnyBeat_IsNull()
    {
        var repository = new WorkerHeartbeatRepository(Context);

        var lastBeat = await repository.GetLastBeatAsync(WorkerName);

        Assert.That(lastBeat, Is.Null);
    }

    [Test]
    public async Task Beat_ThenGetLastBeat_ReturnsTheBeat()
    {
        var repository = new WorkerHeartbeatRepository(Context);
        var at = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        await repository.BeatAsync(WorkerName, at);

        Assert.That(await repository.GetLastBeatAsync(WorkerName), Is.EqualTo(at));
    }

    [Test]
    public async Task BeatingRepeatedly_KeepsOneRow()
    {
        var repository = new WorkerHeartbeatRepository(Context);
        var first = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        await repository.BeatAsync(WorkerName, first);
        await repository.BeatAsync(WorkerName, first.AddMinutes(1));
        await repository.BeatAsync(WorkerName, first.AddMinutes(2));

        Assert.That(await Context.Set<WorkerHeartbeat>().CountAsync(), Is.EqualTo(1));
        Assert.That(await repository.GetLastBeatAsync(WorkerName), Is.EqualTo(first.AddMinutes(2)));
    }

    [Test]
    public async Task BeatsFromDifferentHosts_AreSeparateRows()
    {
        var repository = new WorkerHeartbeatRepository(Context);
        var at = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        await repository.BeatAsync(WorkerName, at);
        await repository.BeatAsync("nornis-other", at.AddMinutes(5));

        // Keyed by host name, so a second deployable can start beating without a migration.
        Assert.That(await repository.GetLastBeatAsync(WorkerName), Is.EqualTo(at));
    }
}
