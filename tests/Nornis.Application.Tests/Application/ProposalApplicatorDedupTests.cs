using System.Text.Json;
using Nornis.Application.Application;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Application;

/// <summary>
/// Apply-time dedup: when several sources are readied together they all extract against the
/// same stale canon and each proposes its own copy of every entity. The applicator is the
/// backstop — an accepted Create binds to the artifact already in canon instead of minting a
/// twin, adding provenance without touching the existing artifact's vetted fields.
///
/// Candidate visibility is scoped to the SOURCE'S AUTHOR, not the accepting reviewer, so a GM
/// accepting a player's note can never bind it to something the player cannot see.
/// </summary>
[TestFixture]
public class ProposalApplicatorDedupTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryWorldMemberRepository _memberRepo = null!;
    private ProposalApplicator _applicator = null!;

    private Guid _worldId;
    private Guid _authorId;
    private Guid _gmId;
    private Source _source = null!;
    private ReviewBatch _batch = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();
        _memberRepo = new InMemoryWorldMemberRepository();

        _applicator = new ProposalApplicator(
            _artifactRepo,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            _sourceRefRepo,
            _sourceRepo,
            new InMemorySourceAttachmentRepository(),
            new InMemoryMapPlacemarkRepository(),
            _memberRepo);

        _worldId = Guid.NewGuid();
        _authorId = Guid.NewGuid();
        _gmId = Guid.NewGuid();

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 4: the harbor again",
            Body = "We went back to Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _authorId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _sourceRepo.Seed(_source);

        _batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        SeedMember(_authorId, WorldRole.Player);
        SeedMember(_gmId, WorldRole.GM);
    }

    [Test]
    public async Task ExactDuplicate_BindsToExistingAndInsertsNothing()
    {
        var existing = SeedArtifact("Black Harbor", ArtifactType.Location, summary: "A vetted summary.");

        var proposal = MakeCreate("Black Harbor", "Location", summary: "A fresh, worse summary.");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(1), "no second artifact may be inserted");
        Assert.That(result.Value!.EntityId, Is.EqualTo(existing.Id));
        Assert.That(result.Value.MatchedExistingArtifact, Is.True);
        Assert.That(proposal.TargetId, Is.EqualTo(existing.Id));
        Assert.That(proposal.AppliedToExistingArtifact, Is.True);

        var reference = _sourceRefRepo.References
            .Single(r => r.TargetType == SourceReferenceTargetType.Artifact);
        Assert.That(reference.TargetId, Is.EqualTo(existing.Id));
        Assert.That(reference.SourceId, Is.EqualTo(_source.Id),
            "the match still records this source's provenance on the existing artifact");
    }

    [Test]
    public async Task Match_LeavesTheExistingArtifactUntouched()
    {
        var existing = SeedArtifact(
            "Black Harbor", ArtifactType.Location,
            summary: "A vetted summary.", visibility: VisibilityScope.PartyVisible, confidence: 0.95m);
        var updatedAtBefore = existing.UpdatedAt;

        var proposal = MakeCreate(
            "Black Harbor", "Location",
            summary: "A fresh, worse summary.", visibility: "Private", confidence: 0.3m);
        await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        var after = _artifactRepo.Artifacts.Single();
        Assert.That(after.Summary, Is.EqualTo("A vetted summary."));
        Assert.That(after.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(after.Confidence, Is.EqualTo(0.95m));
        Assert.That(after.UpdatedAt, Is.EqualTo(updatedAtBefore));
    }

    [TestCase("black harbor")]
    [TestCase("  Black Harbor  ")]
    [TestCase("Black   Harbor")]
    public async Task CaseAndWhitespaceVariants_Match(string proposedName)
    {
        var existing = SeedArtifact("Black Harbor", ArtifactType.Location);

        var proposal = MakeCreate(proposedName, "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.True);
        Assert.That(result.Value.EntityId, Is.EqualTo(existing.Id));
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DifferentArtifactType_SameName_CreatesNew()
    {
        SeedArtifact("Black Harbor", ArtifactType.Location);

        var proposal = MakeCreate("Black Harbor", "Faction");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
        Assert.That(proposal.AppliedToExistingArtifact, Is.False);
    }

    [Test]
    public async Task ArticleDifference_CreatesNew()
    {
        SeedArtifact("The Salt Factor", ArtifactType.Storyline);

        var proposal = MakeCreate("Salt Factor", "Storyline");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False,
            "article stripping is deliberately left to manual merge");
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ArchivedOrNonActiveSameName_CreatesNew()
    {
        SeedArtifact("Black Harbor", ArtifactType.Location, status: ArtifactStatus.Archived);

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
    }

    #region Visibility is the author's, not the reviewer's

    [Test]
    public async Task PlayerAuthoredSource_GmOnlySameNameArtifact_CreatesNewEvenWhenAGmAccepts()
    {
        // The GM reviewing this can see the hidden artifact. Binding to it would tell the
        // player it exists and file their note as provenance for canon they were never shown.
        SeedArtifact("Black Harbor", ArtifactType.Location, visibility: VisibilityScope.GMOnly);

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
        Assert.That(proposal.TargetId, Is.Not.EqualTo(_artifactRepo.Artifacts[0].Id));
    }

    [Test]
    public async Task AnotherUsersPrivateSameNameArtifact_CreatesNew()
    {
        SeedArtifact(
            "Black Harbor", ArtifactType.Location,
            visibility: VisibilityScope.Private, createdByUserId: Guid.NewGuid());

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task PrivateSource_MatchesTheAuthorsOwnPrivateArtifact()
    {
        _source.Visibility = VisibilityScope.Private;
        var existing = SeedArtifact(
            "Black Harbor", ArtifactType.Location,
            visibility: VisibilityScope.Private, createdByUserId: _authorId);

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.True);
        Assert.That(result.Value.EntityId, Is.EqualTo(existing.Id));
    }

    [Test]
    public async Task GmAuthoredGmNote_MatchesAGmOnlyArtifact()
    {
        _source.CreatedByUserId = _gmId;
        _source.Visibility = VisibilityScope.GMOnly;
        var existing = SeedArtifact("Black Harbor", ArtifactType.Location, visibility: VisibilityScope.GMOnly);

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.True);
        Assert.That(result.Value.EntityId, Is.EqualTo(existing.Id));
    }

    #endregion

    #region A match may never reach wider than the source's own audience

    [Test]
    public async Task PrivateSource_DoesNotBindToAPartyVisibleArtifact()
    {
        // Matching attaches this source's verbatim extraction quote to the artifact, and
        // artifact detail serves those quotes to everyone who can see the artifact. Binding a
        // private note to public canon would publish the note's own words to the whole world.
        _source.Visibility = VisibilityScope.Private;
        SeedArtifact("Black Harbor", ArtifactType.Location, visibility: VisibilityScope.PartyVisible);

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task PartyVisibleSource_DoesNotBindToTheAuthorsOwnPrivateArtifact()
    {
        _source.Visibility = VisibilityScope.PartyVisible;
        SeedArtifact(
            "Black Harbor", ArtifactType.Location,
            visibility: VisibilityScope.Private, createdByUserId: _authorId);

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False,
            "a public note's provenance must not be filed against a private artifact");
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GmAuthoredPartyVisibleRecap_DoesNotBindToAGmOnlyArtifact()
    {
        // The GM can see the hidden artifact, but this note is for the party. Binding would
        // funnel party-facing session content into GM-hidden canon with nothing on screen.
        _source.CreatedByUserId = _gmId;
        _source.Visibility = VisibilityScope.PartyVisible;
        SeedArtifact("Silverfang", ArtifactType.Character, visibility: VisibilityScope.GMOnly);

        var proposal = MakeCreate("Silverfang", "Character");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GmOnlySource_MayStillBindToPartyVisibleCanon()
    {
        // Binding downward is legal and load-bearing: a GM walking a backlog import writes
        // GM-only notes about locations the party already knows, and each one must attach to
        // the existing artifact rather than mint a private twin.
        //
        // The note's excerpt does ride along onto a party-visible artifact — that is inherent
        // to provenance, not a reason to refuse the match. It is contained at the READ path
        // instead: artifact detail drops any reference whose source the viewer may not see.
        // See ArtifactDetailProvenanceVisibilityTests.
        _source.CreatedByUserId = _gmId;
        _source.Visibility = VisibilityScope.GMOnly;
        var existing = SeedArtifact("Black Harbor", ArtifactType.Location, visibility: VisibilityScope.PartyVisible);

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.True);
        Assert.That(result.Value.EntityId, Is.EqualTo(existing.Id));
    }

    [Test]
    public async Task PlayerAuthoredGmOnlySource_StillCannotBindToAGmOnlyArtifact()
    {
        // The source gate would allow GMOnly; the author gate must still refuse, because the
        // Player who wrote it may not see GM-only canon.
        _source.Visibility = VisibilityScope.GMOnly;
        SeedArtifact("Black Harbor", ArtifactType.Location, visibility: VisibilityScope.GMOnly);

        var proposal = MakeCreate("Black Harbor", "Location");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AuthorWithNoMembership_FallsBackToPlayerSight()
    {
        // Author left the world: a party-visible artifact still matches, a GM-only one does not.
        // The source is GMOnly so the source gate permits GMOnly — only the author gate blocks it.
        _source.Visibility = VisibilityScope.GMOnly;
        _memberRepo.Members.ToList().ForEach(m => _memberRepo.RemoveAsync(m).GetAwaiter().GetResult());
        SeedArtifact("Ledger of Tides", ArtifactType.Item, visibility: VisibilityScope.GMOnly);
        var visible = SeedArtifact("Black Harbor", ArtifactType.Location, visibility: VisibilityScope.PartyVisible);

        var hidden = await _applicator.ApplyAsync(
            MakeCreate("Ledger of Tides", "Item"), _batch, GmFilter(), CancellationToken.None);
        var shown = await _applicator.ApplyAsync(
            MakeCreate("Black Harbor", "Location"), _batch, GmFilter(), CancellationToken.None);

        Assert.That(hidden.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(shown.Value!.MatchedExistingArtifact, Is.True);
        Assert.That(shown.Value.EntityId, Is.EqualTo(visible.Id));
    }

    #endregion

    [Test]
    public async Task MultipleCandidates_PrefersExactCaseThenOldest()
    {
        var oldLowercase = SeedArtifact(
            "black harbor", ArtifactType.Location, createdAt: DateTimeOffset.UtcNow.AddDays(-10));
        var newerExactCase = SeedArtifact(
            "Black Harbor", ArtifactType.Location, createdAt: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await _applicator.ApplyAsync(
            MakeCreate("Black Harbor", "Location"), _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.EntityId, Is.EqualTo(newerExactCase.Id),
            "exact-case match wins over the older row");
        Assert.That(oldLowercase.Id, Is.Not.EqualTo(result.Value.EntityId));
    }

    [Test]
    public async Task MultipleCandidates_NoExactCase_PicksTheOldest()
    {
        var oldest = SeedArtifact(
            "black harbor", ArtifactType.Location, createdAt: DateTimeOffset.UtcNow.AddDays(-10));
        SeedArtifact("BLACK HARBOR", ArtifactType.Location, createdAt: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await _applicator.ApplyAsync(
            MakeCreate("Black  Harbor", "Location"), _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.EntityId, Is.EqualTo(oldest.Id));
    }

    [Test]
    public async Task NoDuplicate_StillCreatesAndFlagsFalse()
    {
        var proposal = MakeCreate("Captain Voss", "Character");
        var result = await _applicator.ApplyAsync(proposal, _batch, GmFilter(), CancellationToken.None);

        Assert.That(result.Value!.MatchedExistingArtifact, Is.False);
        Assert.That(proposal.AppliedToExistingArtifact, Is.False);
        Assert.That(_artifactRepo.Artifacts.Single().Name, Is.EqualTo("Captain Voss"));
    }

    private VisibilityFilter GmFilter() => VisibilityFilter.ForRole(WorldRole.GM, _gmId);

    private void SeedMember(Guid userId, WorldRole role) =>
        _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-30)
        }).GetAwaiter().GetResult();

    private Artifact SeedArtifact(
        string name,
        ArtifactType type,
        string? summary = null,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        ArtifactStatus status = ArtifactStatus.Active,
        decimal? confidence = null,
        Guid? createdByUserId = null,
        DateTimeOffset? createdAt = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Summary = summary,
            Visibility = visibility,
            Status = status,
            Confidence = confidence,
            CreatedByUserId = createdByUserId ?? _gmId,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-5),
            UpdatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-5)
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private ReviewProposal MakeCreate(
        string name, string type, string? summary = null, string? visibility = null, decimal? confidence = null)
    {
        var payload = new CreateArtifactPayload(name, type, summary, visibility, confidence);
        return new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = JsonSerializer.Serialize(payload),
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
    }
}
