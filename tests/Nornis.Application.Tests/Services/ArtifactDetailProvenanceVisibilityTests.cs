using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// A source reference carries the verbatim extraction Quote from the note it came from, and
/// artifact detail is served to Players, Observers, and — through the public world page —
/// anonymous visitors. So a PartyVisible artifact cited by a GM-only or another player's
/// Private note must not hand that note's words to everyone who can see the artifact.
///
/// Withholding only the title was never enough: a titleless quote IS the leak. The whole
/// reference row is dropped for readers who may not see its source, which also costs them
/// nothing — a reference they cannot attribute tells them nothing.
///
/// A PartyVisible artifact can legitimately be cited by a hidden note in several ways: an
/// accepted UpdateArtifact or AddFact from a GM note, or apply-time dedup binding a GM
/// backlog import's CreateArtifact to canon that already existed.
/// </summary>
[TestFixture]
public class ArtifactDetailProvenanceVisibilityTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private ArtifactService _service = null!;

    private Guid _worldId;
    private Guid _gmId;
    private Guid _playerId;
    private Guid _otherPlayerId;
    private Artifact _artifact = null!;

    /// <summary>The anonymous public world page reads as Observer with an empty user id.</summary>
    private static readonly Guid AnonymousUserId = Guid.Empty;

    private const string GmQuote = "Silverfang is secretly the harbormaster's brother.";
    private const string PrivateQuote = "I pocketed the mayor's ledger while nobody watched.";
    private const string PartyQuote = "We docked at Black Harbor before dawn.";

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();

        _service = new ArtifactService(
            _artifactRepo, _factRepo, new InMemoryArtifactRelationshipRepository(),
            _sourceRefRepo, _sourceRepo, new InMemoryCharacterRepository(),
            new InMemoryWorldMemberRepository(), new InMemoryStorylineCampaignRepository(),
            new InMemoryCampaignRepository());

        _worldId = Guid.NewGuid();
        _gmId = Guid.NewGuid();
        _playerId = Guid.NewGuid();
        _otherPlayerId = Guid.NewGuid();

        // The artifact everyone can see. Its provenance is where the leak lived.
        _artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Location,
            Name = "Black Harbor",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };
        _artifactRepo.Seed(_artifact);

        SeedSourceWithReference(VisibilityScope.GMOnly, _gmId, GmQuote, "GM prep");
        SeedSourceWithReference(VisibilityScope.Private, _otherPlayerId, PrivateQuote, "Sable's diary");
        SeedSourceWithReference(VisibilityScope.PartyVisible, _playerId, PartyQuote, "Session 12");
    }

    [Test]
    public async Task Gm_SeesEveryQuote()
    {
        var quotes = await QuotesFor(_gmId, WorldRole.GM);

        Assert.That(quotes, Is.EquivalentTo(new[] { GmQuote, PrivateQuote, PartyQuote }));
    }

    [Test]
    public async Task Player_SeesOnlyThePartyVisibleQuote()
    {
        var quotes = await QuotesFor(_playerId, WorldRole.Player);

        Assert.That(quotes, Is.EquivalentTo(new[] { PartyQuote }));
        Assert.That(quotes, Does.Not.Contain(GmQuote), "a GM note's excerpt must not reach a player");
        Assert.That(quotes, Does.Not.Contain(PrivateQuote), "nor another player's private note");
    }

    [Test]
    public async Task Observer_SeesOnlyThePartyVisibleQuote()
    {
        var quotes = await QuotesFor(Guid.NewGuid(), WorldRole.Observer);

        Assert.That(quotes, Is.EquivalentTo(new[] { PartyQuote }));
    }

    [Test]
    public async Task AnonymousPublicViewer_SeesOnlyThePartyVisibleQuote()
    {
        // The public world page reads through this same method as Observer/Guid.Empty.
        var quotes = await QuotesFor(AnonymousUserId, WorldRole.Observer);

        Assert.That(quotes, Is.EquivalentTo(new[] { PartyQuote }));
        Assert.That(quotes, Does.Not.Contain(GmQuote));
        Assert.That(quotes, Does.Not.Contain(PrivateQuote));
    }

    [Test]
    public async Task AnonymousViewer_DoesNotMatchAnUnattributedPrivateSource()
    {
        // A Private source whose CreatedByUserId is empty must not read as "owned by" the
        // anonymous viewer, whose id is also empty. Unattributable private content fails closed.
        SeedSourceWithReference(VisibilityScope.Private, Guid.Empty, "An orphaned private note.", "Orphan");

        var quotes = await QuotesFor(AnonymousUserId, WorldRole.Observer);

        Assert.That(quotes, Is.EquivalentTo(new[] { PartyQuote }));
    }

    [Test]
    public async Task PrivateSourceAuthor_SeesTheirOwnQuote()
    {
        var quotes = await QuotesFor(_otherPlayerId, WorldRole.Player);

        Assert.That(quotes, Is.EquivalentTo(new[] { PrivateQuote, PartyQuote }));
    }

    [Test]
    public async Task ReferenceToADeletedSource_IsDroppedEvenForTheGm()
    {
        // Fail closed: a reference we cannot attribute at all is not shown.
        _sourceRefRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = Guid.NewGuid(),
            TargetType = SourceReferenceTargetType.Artifact,
            TargetId = _artifact.Id,
            Quote = "A quote from a source that no longer exists.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var quotes = await QuotesFor(_gmId, WorldRole.GM);

        Assert.That(quotes, Is.EquivalentTo(new[] { GmQuote, PrivateQuote, PartyQuote }));
    }

    [Test]
    public async Task QuoteOnAFactFromAHiddenSource_IsAlsoDropped()
    {
        // The pre-existing variant: the FACT is party-visible, but the note that produced it
        // is GM-only, so its excerpt is still the GM's.
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = _artifact.Id,
            Predicate = "controlled by",
            Value = "the harbor guild",
            TruthState = TruthState.Likely,
            Visibility = VisibilityScope.PartyVisible,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _factRepo.Seed(fact);

        var gmSource = SeedSource(VisibilityScope.GMOnly, _gmId, "GM prep 2");
        _sourceRefRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = gmSource.Id,
            TargetType = SourceReferenceTargetType.ArtifactFact,
            TargetId = fact.Id,
            Quote = "The guild's grip is the thing the party has not worked out yet.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var playerQuotes = await QuotesFor(_playerId, WorldRole.Player);
        var gmQuotes = await QuotesFor(_gmId, WorldRole.GM);

        Assert.That(playerQuotes, Is.EquivalentTo(new[] { PartyQuote }),
            "the fact is visible but its GM-authored excerpt is not");
        Assert.That(gmQuotes, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task VisibleReferences_StillCarryTheirSourceTitle()
    {
        var detail = await GetDetailAsync(_playerId, WorldRole.Player);

        var reference = detail.SourceReferences.Single();
        Assert.That(detail.SourceTitles[reference.SourceId], Is.EqualTo("Session 12"),
            "dropping hidden rows must not cost the surviving ones their title");
    }

    private async Task<Nornis.Application.Models.ArtifactDetail> GetDetailAsync(Guid userId, WorldRole role)
    {
        var result = await _service.GetDetailAsync(_artifact.Id, _worldId, userId, role, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True);
        return result.Value!;
    }

    private async Task<IReadOnlyList<string>> QuotesFor(Guid userId, WorldRole role)
    {
        var detail = await GetDetailAsync(userId, role);
        return detail.SourceReferences.Select(r => r.Quote).OfType<string>().ToList();
    }

    private Source SeedSource(VisibilityScope visibility, Guid createdByUserId, string title)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = title,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private void SeedSourceWithReference(
        VisibilityScope visibility, Guid createdByUserId, string quote, string title)
    {
        var source = SeedSource(visibility, createdByUserId, title);
        _sourceRefRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TargetType = SourceReferenceTargetType.Artifact,
            TargetId = _artifact.Id,
            Quote = quote,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });
    }
}
