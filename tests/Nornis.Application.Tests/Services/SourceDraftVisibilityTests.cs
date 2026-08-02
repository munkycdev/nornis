using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Regression: an unsubmitted Draft source is readable only by its author and the GM,
/// whatever its Visibility says.
///
/// Visibility describes the canon a source will yield once extracted — not who may read
/// the raw note while it waits for review. Capture's draft window is seconds, so this
/// barely mattered; the campaign backlog import parks an entire backlog at Draft for as
/// long as the GM takes to walk it note by note. The same list is served to the anonymous
/// public world page (PublicController reads as Observer with Guid.Empty), so before this
/// rule a GM pasting thirty sessions of unvetted notes published the campaign's whole
/// future to the internet before reading the first one. Found 2026-07-26 reviewing the
/// import flow.
/// </summary>
[TestFixture]
public class SourceDraftVisibilityTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();
    private static readonly Guid PlayerUserId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private SourceService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _sut = new SourceService(_sourceRepository, new InMemoryWorldMemberRepository(),
            new InMemoryCampaignRepository(), new FakeExtractionQueueClient(),
            new InMemoryReviewBatchRepository(), new InMemoryReviewProposalRepository(), new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(), NullLogger<SourceService>.Instance);
    }

    private Source Seed(SourceProcessingStatus status, Guid authorId,
        VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.ImportedNote,
            Title = "Session 14 — the phylactery",
            Body = "Aldric's phylactery is hidden beneath Black Harbor.",
            Visibility = visibility,
            ProcessingStatus = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = authorId
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private async Task<bool> CanList(Guid userId, WorldRole role, Guid sourceId)
    {
        var result = await _sut.ListSummariesByWorldAsync(WorldId, userId, role, CancellationToken.None);
        return result.Value!.Any(s => s.Id == sourceId);
    }

    private async Task<bool> CanGet(Guid userId, WorldRole role, Guid sourceId)
    {
        var result = await _sut.GetByIdAsync(sourceId, WorldId, userId, role, CancellationToken.None);
        return result.IsSuccess;
    }

    [Test]

    [Category("Authorization")]
    public async Task HeldImportNote_IsHiddenFromPlayersAndObservers()
    {
        var held = Seed(SourceProcessingStatus.Draft, GmUserId);

        Assert.That(await CanList(PlayerUserId, WorldRole.Player, held.Id), Is.False,
            "A Player must not see a PartyVisible note still held at Draft.");
        Assert.That(await CanList(PlayerUserId, WorldRole.Observer, held.Id), Is.False,
            "An Observer must not see a PartyVisible note still held at Draft.");
        Assert.That(await CanGet(PlayerUserId, WorldRole.Player, held.Id), Is.False,
            "Fetching the held note directly must 404 for a Player.");
    }

    [Test]

    [Category("Authorization")]
    public async Task HeldImportNote_IsHiddenFromTheAnonymousPublicPage()
    {
        // PublicController reads as Observer with Guid.Empty for the user id.
        var held = Seed(SourceProcessingStatus.Draft, GmUserId);

        Assert.That(await CanList(Guid.Empty, WorldRole.Observer, held.Id), Is.False);
        Assert.That(await CanGet(Guid.Empty, WorldRole.Observer, held.Id), Is.False);
    }

    [Test]
    public async Task HeldNote_RemainsVisibleToItsAuthorAndTheGm()
    {
        var held = Seed(SourceProcessingStatus.Draft, GmUserId);
        var playerDraft = Seed(SourceProcessingStatus.Draft, PlayerUserId);

        Assert.That(await CanList(GmUserId, WorldRole.GM, held.Id), Is.True,
            "The GM walking the import must still see the notes waiting in it.");
        Assert.That(await CanList(PlayerUserId, WorldRole.Player, playerDraft.Id), Is.True,
            "An author always sees their own unsubmitted draft.");
    }

    [Test]
    public async Task OncePastDraft_NormalVisibilityRulesResume()
    {
        foreach (var status in (SourceProcessingStatus[])[
            SourceProcessingStatus.Ready,
            SourceProcessingStatus.Queued,
            SourceProcessingStatus.Processing,
            SourceProcessingStatus.Processed,
            SourceProcessingStatus.Failed])
        {
            var source = Seed(status, GmUserId);

            Assert.That(await CanList(PlayerUserId, WorldRole.Player, source.Id), Is.True,
                $"A PartyVisible source at {status} is shared knowledge and must stay visible.");
        }
    }

    [Test]
    public async Task TheDraftGateOnlyNarrows_ItNeverWidens()
    {
        // A GM-only draft stays GM-only even for the player who somehow authored it, and a
        // processed GM-only note is still not a player's to read.
        var gmOnlyDraftByPlayer = Seed(SourceProcessingStatus.Draft, PlayerUserId, VisibilityScope.GMOnly);
        var gmOnlyProcessed = Seed(SourceProcessingStatus.Processed, GmUserId, VisibilityScope.GMOnly);

        Assert.That(await CanList(PlayerUserId, WorldRole.Player, gmOnlyDraftByPlayer.Id), Is.False,
            "The draft gate must not hand a player GM-only material just because they created it.");
        Assert.That(await CanList(PlayerUserId, WorldRole.Player, gmOnlyProcessed.Id), Is.False);
    }

    [Test]
    public async Task AnUnattributedPrivateDraft_DoesNotMatchTheAnonymousReader()
    {
        // Guid.Empty is the anonymous reader's id; a Private row with no real owner must
        // fail closed rather than read as "created by" that reader.
        var unattributed = Seed(SourceProcessingStatus.Draft, Guid.Empty, VisibilityScope.Private);

        Assert.That(await CanList(Guid.Empty, WorldRole.Observer, unattributed.Id), Is.False);
        Assert.That(await CanGet(Guid.Empty, WorldRole.Observer, unattributed.Id), Is.False);
    }
}
