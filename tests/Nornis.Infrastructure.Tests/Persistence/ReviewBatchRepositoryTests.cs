using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// One extraction batch per source, held by the database rather than by a read the two racing
/// runs both pass. Tested here and nowhere else on purpose: the invariant *is*
/// IX_ReviewBatches_SourceId_Extraction, so a fixture with a fake repository would be asserting
/// against its own fake.
/// </summary>
[TestFixture]
public class ReviewBatchRepositoryTests : IntegrationTestBase
{
    private (World World, Source Source) SeedWorldAndSource()
    {
        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        };
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Vespergale",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = user.Id,
            RowVersion = []
        };
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Type = SourceType.SessionNote,
            Title = "Session 5",
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedAt = now,
            CreatedByUserId = user.Id
        };
        Context.Users.Add(user);
        Context.Worlds.Add(world);
        Context.Sources.Add(source);
        Context.SaveChanges();
        return (world, source);
    }

    private static ReviewBatch MakeBatch(Guid worldId, Guid sourceId, string? kind = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = sourceId,
            Status = ReviewBatchStatus.Pending,
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow
        };

    [Test]
    public async Task TryCreateExtractionBatch_SecondBatchForTheSameSource_ReturnsNull()
    {
        var (world, source) = SeedWorldAndSource();
        var repo = new ReviewBatchRepository(Context);

        var first = await repo.TryCreateExtractionBatchAsync(MakeBatch(world.Id, source.Id));
        var second = await repo.TryCreateExtractionBatchAsync(MakeBatch(world.Id, source.Id));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null, "the first run owns the source");
            Assert.That(second, Is.Null,
                "a redelivery that ran concurrently must lose here rather than commit a second batch");
        });
    }

    [Test]
    public async Task TryCreateExtractionBatch_AfterALoss_TheContextIsStillUsable()
    {
        var (world, source) = SeedWorldAndSource();
        var repo = new ReviewBatchRepository(Context);
        await repo.TryCreateExtractionBatchAsync(MakeBatch(world.Id, source.Id));

        await repo.TryCreateExtractionBatchAsync(MakeBatch(world.Id, source.Id));

        // A failed SaveChanges leaves the rejected entity Added in the change tracker, so the
        // next save in the same scoped context would retry it and throw again — the poisoned
        // context this repository clears on the way out.
        Assert.DoesNotThrowAsync(() => repo.CreateAsync(MakeBatch(world.Id, source.Id, kind: "Reveal")));
    }

    [Test]
    public async Task TryCreateExtractionBatch_DoesNotCollideWithKindedBatches()
    {
        var (world, source) = SeedWorldAndSource();
        var repo = new ReviewBatchRepository(Context);
        await repo.CreateAsync(MakeBatch(world.Id, source.Id, kind: "Reveal"));
        await repo.CreateAsync(MakeBatch(world.Id, source.Id, kind: "ContinuityFix"));

        var extraction = await repo.TryCreateExtractionBatchAsync(MakeBatch(world.Id, source.Id));

        // The index is filtered on Kind IS NULL because a source can carry any number of reveal,
        // fix and backfill batches — only the extraction batch is one-per-source.
        Assert.That(extraction, Is.Not.Null);
    }

    [Test]
    public async Task GetBySourceId_ReturnsTheExtractionBatchAndNotAKindedOne()
    {
        var (world, source) = SeedWorldAndSource();
        var repo = new ReviewBatchRepository(Context);
        await repo.CreateAsync(MakeBatch(world.Id, source.Id, kind: "Reveal"));
        var extraction = await repo.TryCreateExtractionBatchAsync(MakeBatch(world.Id, source.Id));

        var found = await repo.GetBySourceIdAsync(source.Id);

        Assert.That(found?.Id, Is.EqualTo(extraction!.Id));
    }
}
