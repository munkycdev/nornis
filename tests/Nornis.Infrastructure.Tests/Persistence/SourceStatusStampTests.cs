using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// <c>StatusChangedAt</c> is stamped by the DbContext rather than by callers, because it is
/// what the Queued-wedge gate reads and there are thirty-eight places that change a source's
/// status. A missed stamp would not fail anything loudly — it would leave a wedged source
/// looking permanently fresh, which is the bug it was added to fix.
///
/// Tested here rather than against a fake: the whole mechanism *is* the change tracker, so a
/// fixture that fakes persistence would be asserting against nothing.
/// </summary>
[TestFixture]
public class SourceStatusStampTests : IntegrationTestBase
{
    private Source SeedSource(SourceProcessingStatus status, DateTimeOffset? stampedAt = null)
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
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            CreatedAt = now,
            CreatedByUserId = user.Id
        };
        Context.Users.Add(user);
        Context.Worlds.Add(world);
        Context.Sources.Add(source);
        Context.SaveChanges();

        if (stampedAt is not null)
        {
            source.StatusChangedAt = stampedAt;
            Context.SaveChanges();
        }

        return source;
    }

    [Test]
    public async Task ChangingTheStatus_StampsWithoutBeingAsked()
    {
        var source = SeedSource(SourceProcessingStatus.Draft);
        var before = source.StatusChangedAt;

        // The scoped writer, which is what most callers reach for. Nothing here mentions the
        // timestamp — that is the point.
        await new SourceRepository(Context).UpdateProcessingStatusAsync(
            source.Id, SourceProcessingStatus.Queued);

        var reloaded = await CreateNewContext().Sources.FirstAsync(s => s.Id == source.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
            Assert.That(reloaded.StatusChangedAt, Is.Not.EqualTo(before));
            Assert.That(reloaded.StatusChangedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task ChangingSomethingElse_LeavesTheStampAlone()
    {
        var stamped = DateTimeOffset.UtcNow.AddHours(-3);
        var source = SeedSource(SourceProcessingStatus.Queued, stamped);

        // Editing a title must not make a wedged source look freshly queued — otherwise any
        // passing edit resets the clock the gate is reading.
        var tracked = await Context.Sources.FirstAsync(s => s.Id == source.Id);
        tracked.Title = "Session 5 — revised";
        await Context.SaveChangesAsync();

        var reloaded = await CreateNewContext().Sources.FirstAsync(s => s.Id == source.Id);
        Assert.That(reloaded.StatusChangedAt, Is.EqualTo(stamped).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task ClaimingForExtraction_StampsToo()
    {
        var source = SeedSource(SourceProcessingStatus.Queued, DateTimeOffset.UtcNow.AddHours(-3));

        // TryClaimForExtractionAsync uses ExecuteUpdate, which bypasses the change tracker
        // entirely — so the DbContext stamp never sees it and it carries its own SetProperty.
        // Without that, a source that is genuinely Processing would keep the timestamp of when
        // it was Queued.
        var claimed = await new SourceRepository(Context).TryClaimForExtractionAsync(source.Id);

        var reloaded = await CreateNewContext().Sources.FirstAsync(s => s.Id == source.Id);
        Assert.Multiple(() =>
        {
            Assert.That(claimed, Is.True);
            Assert.That(reloaded.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processing));
            Assert.That(reloaded.StatusChangedAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
    }
}
