using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The continuity-audit claim exists to stop two API hosts spending a paid AI call on the same
/// world. Its whole value is in the atomicity of the conditional UPDATE, so these tests run the
/// real query against a relational provider (SQLite) rather than a fake — an in-memory
/// read-then-write would pass while the production behaviour was broken.
/// </summary>
[TestFixture]
public class WorldRepositoryContinuityAuditClaimTests : IntegrationTestBase
{
    private Guid _worldId;
    private WorldRepository _repository = null!;

    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tag = Guid.NewGuid().ToString("N");

        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        Context.Users.Add(new User
        {
            Id = userId,
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Vespergale Reach",
            CreatedByUserId = userId,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await Context.SaveChangesAsync();

        _repository = new WorldRepository(Context);
    }

    [Test]
    public async Task UnclaimedWorld_IsClaimed()
    {
        var claimed = await _repository.TryClaimContinuityAuditAsync(
            _worldId, Now, staleBefore: Now.AddHours(-2));

        Assert.That(claimed, Is.True);

        var world = await Context.Worlds.AsNoTracking().SingleAsync(w => w.Id == _worldId);
        Assert.That(world.ContinuityAuditClaimedAt, Is.EqualTo(Now));
    }

    [Test]
    public async Task FreshClaim_BlocksASecondCaller()
    {
        await _repository.TryClaimContinuityAuditAsync(_worldId, Now, staleBefore: Now.AddHours(-2));

        // A second host, one minute later, with the same two-hour staleness window.
        var second = Now.AddMinutes(1);
        var claimed = await _repository.TryClaimContinuityAuditAsync(
            _worldId, second, staleBefore: second.AddHours(-2));

        Assert.That(claimed, Is.False, "the second host must skip rather than pay for a duplicate assessment");

        var world = await Context.Worlds.AsNoTracking().SingleAsync(w => w.Id == _worldId);
        Assert.That(world.ContinuityAuditClaimedAt, Is.EqualTo(Now), "the winner's claim must not be overwritten");
    }

    [Test]
    public async Task ClaimOlderThanTheStalenessWindow_IsTakenOver()
    {
        // A host that claimed and then died. Without takeover the world would never be audited again.
        var abandoned = Now.AddHours(-6);
        await _repository.TryClaimContinuityAuditAsync(_worldId, abandoned, staleBefore: abandoned.AddHours(-2));

        var claimed = await _repository.TryClaimContinuityAuditAsync(
            _worldId, Now, staleBefore: Now.AddHours(-2));

        Assert.That(claimed, Is.True);

        var world = await Context.Worlds.AsNoTracking().SingleAsync(w => w.Id == _worldId);
        Assert.That(world.ContinuityAuditClaimedAt, Is.EqualTo(Now));
    }

    [Test]
    public async Task ClaimExactlyAtTheStalenessBoundary_IsTakenOver()
    {
        // The predicate is <=, so a claim landing exactly on the cutoff is reclaimable. Pinned
        // because flipping it to < would strand a world for a full extra timeout window.
        var boundary = Now.AddHours(-2);
        await _repository.TryClaimContinuityAuditAsync(_worldId, boundary, staleBefore: boundary.AddHours(-2));

        var claimed = await _repository.TryClaimContinuityAuditAsync(
            _worldId, Now, staleBefore: boundary);

        Assert.That(claimed, Is.True);
    }

    [Test]
    public async Task UnknownWorld_IsNotClaimed()
    {
        var claimed = await _repository.TryClaimContinuityAuditAsync(
            Guid.NewGuid(), Now, staleBefore: Now.AddHours(-2));

        Assert.That(claimed, Is.False);
    }

    [Test]
    public async Task ConcurrentHostsRacingTheSameWorld_ProduceExactlyOneWinner()
    {
        // The point of the whole mechanism. Separate DbContexts over the same database stand in
        // for two API replicas ticking in the same window — which is exactly what a rolling
        // deploy produces, since both revisions run the background loop while draining overlaps.
        const int hosts = 8;

        var contexts = Enumerable.Range(0, hosts).Select(_ => CreateNewContext()).ToList();
        try
        {
            var repositories = contexts.Select(c => new WorldRepository(c)).ToList();

            var results = await Task.WhenAll(repositories.Select(r =>
                r.TryClaimContinuityAuditAsync(_worldId, Now, staleBefore: Now.AddHours(-2))));

            Assert.That(results.Count(won => won), Is.EqualTo(1),
                "exactly one host may win the claim; any other count means duplicate paid AI calls");
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }
}
