using System.Text.Json;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Application;

/// <summary>
/// The applicator holds the dedup candidate set in memory for the life of one request instead of
/// re-reading it per proposal. Accepting a 50-proposal batch used to run a full by-type artifact
/// listing and a membership lookup on every CreateArtifact.
///
/// <para><b>What these tests are really guarding.</b> The cheap part is the query count. The
/// expensive part is what a stale cache costs: a batch that proposes the same new artifact twice
/// must still bind the second to the first, and any arm that renames, re-statuses or archives an
/// artifact must not leave the cache claiming otherwise. Every test here that asserts a count is
/// paired with one that asserts the behaviour, because a cache that is fast and wrong is worse
/// than the loop it replaced — it mints the duplicate the applicator exists to prevent, and every
/// fact in the batch attached to the wrong copy.</para>
/// </summary>
[TestFixture]
public class ProposalApplicatorDedupCacheTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
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
        _memberRepo = new InMemoryWorldMemberRepository();

        _applicator = new ProposalApplicator(
            _artifactRepo,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceReferenceRepository(),
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
            Title = "Session 9: the salt run",
            Body = "The Salt Factor came up again, twice.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _authorId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

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

    #region Correctness: the cache must not reintroduce the duplicate

    [Test]
    public async Task TwoCreatesOfTheSameNameInOneBatch_TheSecondBindsToTheFirst()
    {
        // This is the failure the whole cache is at risk of causing. Nothing was in canon before
        // the batch, so the candidate set is read empty — and if the first create does not land in
        // it, the second sees an empty set too, and the world ends up with two Salt Factors, each
        // holding half the batch's facts.
        var first = await ApplyAsync(MakeCreate("The Salt Factor", "Storyline"));
        var second = await ApplyAsync(MakeCreate("The Salt Factor", "Storyline"));

        Assert.Multiple(() =>
        {
            Assert.That(first.Value!.MatchedExistingArtifact, Is.False, "the first create mints it");
            Assert.That(second.Value!.MatchedExistingArtifact, Is.True, "the second must bind, not mint");
            Assert.That(second.Value.EntityId, Is.EqualTo(first.Value.EntityId));
            Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task TheSecondCreateMatchesAcrossNameEquivalence_NotJustExactStrings()
    {
        // Two proposals from the same extraction rarely agree on casing or spacing. The cached
        // entry has to be matched by the same name-equivalence policy as a row read from SQL,
        // which it is — the filtering all still runs in memory over the same objects.
        var first = await ApplyAsync(MakeCreate("Black Harbor", "Location"));
        var second = await ApplyAsync(MakeCreate("  black   harbor ", "Location"));

        Assert.That(second.Value!.EntityId, Is.EqualTo(first.Value!.EntityId));
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ACreateOfADifferentType_IsNotConfusedWithTheCachedOne()
    {
        var location = await ApplyAsync(MakeCreate("Black Harbor", "Location"));
        var faction = await ApplyAsync(MakeCreate("Black Harbor", "Faction"));

        Assert.Multiple(() =>
        {
            Assert.That(faction.Value!.MatchedExistingArtifact, Is.False);
            Assert.That(faction.Value.EntityId, Is.Not.EqualTo(location.Value!.EntityId));
            Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ACreatedArtifactTheAuthorCannotSee_IsStillNotMatchable()
    {
        // The cached set is deliberately unfiltered — the visibility gates run at match time — so
        // this checks the gate did not go missing in the move. A Player author cannot see GM-only
        // canon, so a GM-only artifact minted mid-request must not become a match for them, cached
        // or otherwise. The source is GM-only (so the source gate permits GMOnly) but its author is
        // a Player (so the author gate does not).
        _source.Visibility = VisibilityScope.GMOnly;

        var first = await ApplyAsync(MakeCreate("Ledger of Tides", "Item", visibility: "GMOnly"));
        var second = await ApplyAsync(MakeCreate("Ledger of Tides", "Item", visibility: "GMOnly"));

        Assert.Multiple(() =>
        {
            Assert.That(_artifactRepo.Artifacts.All(a => a.Visibility == VisibilityScope.GMOnly), Is.True,
                "the arrangement is only meaningful if the artifacts really are GM-only");
            Assert.That(second.Value!.MatchedExistingArtifact, Is.False,
                "a Player author may not match GM-only canon, cached or otherwise");
            Assert.That(second.Value.EntityId, Is.Not.EqualTo(first.Value!.EntityId));
        });
    }

    [Test]
    public async Task ACreateAgainstOneAuthorsSource_IsVisibleToTheNextAuthorsSource()
    {
        // One accept spans every source in the world, so consecutive proposals routinely come from
        // different authors. A candidate set keyed by author would be warmed for Bob, then updated
        // only for Alice when her create landed, leaving Bob's set blind to it — and Bob's next
        // proposal would mint the duplicate. That is the Salt Factor failure arriving by a second
        // route, with no rename or merge anywhere near it. The set is keyed by (world, type) so
        // this cannot happen.
        var bob = _source;                       // warms the set
        var alice = SourceBy(Guid.NewGuid());
        SeedMember(alice.CreatedByUserId, WorldRole.Player);

        await ApplyAsync(MakeCreate("Tidewatch", "Location"), bob);
        var minted = await ApplyAsync(MakeCreate("Black Harbor", "Location"), alice);
        var second = await ApplyAsync(MakeCreate("Black Harbor", "Location"), bob);

        Assert.Multiple(() =>
        {
            Assert.That(second.Value!.MatchedExistingArtifact, Is.True,
                "Bob's proposal must bind to the artifact Alice's proposal just created");
            Assert.That(second.Value.EntityId, Is.EqualTo(minted.Value!.EntityId));
            Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2), "Tidewatch and one Black Harbor");
        });
    }

    [Test]
    public async Task ACreateWhoseTransactionRolledBack_IsNotOfferedAsAMatch()
    {
        // Each proposal applies in its own transaction and the accept loop carries on after a
        // rollback, on this same applicator. A cached artifact whose insert was undone would still
        // look like canon: the next proposal naming it would be reported as "bound to existing",
        // commit provenance pointing at an id no row carries, and leave every fact on that name
        // unresolvable — with the proposal already Accepted, so nothing can reopen it.
        var ghost = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        // What a rollback leaves behind: the row is gone, the cache is not.
        await _artifactRepo.DeleteAsync(ghost.Value!.EntityId, CancellationToken.None);

        var after = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        Assert.Multiple(() =>
        {
            Assert.That(after.Value!.MatchedExistingArtifact, Is.False);
            Assert.That(after.Value.EntityId, Is.Not.EqualTo(ghost.Value.EntityId));
            Assert.That(_artifactRepo.Artifacts.Single().Name, Is.EqualTo("Black Harbor"),
                "the second proposal must really have minted the artifact");
        });
    }

    [Test]
    public async Task AFailedCreate_NeverEntersTheCacheAtAll()
    {
        // The cheaper half of the same guard: an apply that fails after the insert is rolled back
        // by the caller, so it should not be cached in the first place. Here the pin block names an
        // attachment that does not exist, which fails the apply after the artifact row is written.
        var doomed = MakeCreate("Black Harbor", "Location");
        doomed.ProposedValueJson = JsonSerializer.Serialize(new CreateArtifactPayload(
            "Black Harbor", "Location", Summary: null, Visibility: null, Confidence: null,
            new MapPlacemarkBlock(Guid.NewGuid(), 0.5m, 0.5m, Label: null)));

        var failed = await ApplyAsync(doomed);
        Assert.That(failed.IsSuccess, Is.False, "the arrangement only means anything if the apply fails");

        _artifactRepo.ListByTypeCallCount = 0;
        var after = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        Assert.Multiple(() =>
        {
            Assert.That(after.Value!.MatchedExistingArtifact, Is.False);
            Assert.That(_artifactRepo.ListByTypeCallCount, Is.Zero,
                "the set is still cached — this is about what went into it, not about refetching");
        });
    }

    [Test]
    public async Task RenamingAnArtifactMidBatch_InvalidatesTheCachedSet()
    {
        var existing = SeedArtifact("Black Harbor", ArtifactType.Location);

        // Warm the cache under the old name.
        var bound = await ApplyAsync(MakeCreate("Black Harbor", "Location"));
        Assert.That(bound.Value!.EntityId, Is.EqualTo(existing.Id));

        await ApplyAsync(MakeUpdate(existing.Id, name: "Blackwater Harbor"));

        // The old name is now free. A stale cache would still be holding "Black Harbor" and bind
        // this create to an artifact that no longer carries that name.
        var after = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        Assert.Multiple(() =>
        {
            Assert.That(after.Value!.MatchedExistingArtifact, Is.False);
            Assert.That(after.Value.EntityId, Is.Not.EqualTo(existing.Id));
            Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ArchivingAnArtifactByMerge_InvalidatesTheCachedSet()
    {
        var target = SeedArtifact("Blackwater Harbor", ArtifactType.Location);
        var duplicate = SeedArtifact("Black Harbor", ArtifactType.Location);

        // Warm the cache while both are still active.
        var bound = await ApplyAsync(MakeCreate("Black Harbor", "Location"));
        Assert.That(bound.Value!.EntityId, Is.EqualTo(duplicate.Id));

        await ApplyAsync(MakeMerge(target.Id, duplicate.Id));

        // The merged-away artifact is archived and must stop matching. A stale cache would bind a
        // fresh proposal to an archived row, and the fact would land somewhere nobody reads.
        var after = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        Assert.Multiple(() =>
        {
            Assert.That(_artifactRepo.Artifacts.Single(a => a.Id == duplicate.Id).Status,
                Is.EqualTo(ArtifactStatus.Archived));
            Assert.That(after.Value!.MatchedExistingArtifact, Is.False);
            Assert.That(after.Value.EntityId, Is.Not.EqualTo(duplicate.Id));
        });
    }

    #endregion

    #region The snapshot is confirmed against the database before it is used

    // These four are about the OTHER way a snapshot goes wrong: not this request mutating an
    // artifact — those arms invalidate — but a concurrent request doing it. The code this replaced
    // re-listed before every single create, so it saw those writes for free. Caching gives that up
    // unless the chosen match is re-read, which is what ConfirmMatchAsync does.
    //
    // Mutating the fake's stored artifact directly is exactly that concurrent write: the cache
    // holds a detached copy, so the change is invisible to it until something re-reads.

    [Test]
    public async Task AnArtifactRenamedByAnotherRequest_IsNoLongerAMatch()
    {
        var existing = SeedArtifact("Black Harbor", ArtifactType.Location);
        var bound = await ApplyAsync(MakeCreate("Black Harbor", "Location"));
        Assert.That(bound.Value!.EntityId, Is.EqualTo(existing.Id), "the cache is warm and holding it");

        existing.Name = "Blackwater Harbor";

        var after = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        Assert.Multiple(() =>
        {
            Assert.That(after.Value!.MatchedExistingArtifact, Is.False);
            Assert.That(after.Value.EntityId, Is.Not.EqualTo(existing.Id),
                "binding here would file this source's quote on an artifact that no longer carries the name");
        });
    }

    [Test]
    public async Task AnArtifactArchivedByAnotherRequest_IsNoLongerAMatch()
    {
        var existing = SeedArtifact("Black Harbor", ArtifactType.Location);
        await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        existing.Status = ArtifactStatus.Archived;

        var after = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        Assert.Multiple(() =>
        {
            Assert.That(after.Value!.MatchedExistingArtifact, Is.False);
            Assert.That(after.Value.EntityId, Is.Not.EqualTo(existing.Id),
                "an archived artifact cannot be resolved by name later, so the batch's facts would strand");
        });
    }

    [Test]
    public async Task AnArtifactHiddenByAnotherRequest_IsNoLongerAMatch()
    {
        // The quiet one. Binding a Player's party-visible note to canon that has since been made
        // GM-only would attach their verbatim quote to something they may not see, and resolve the
        // hidden artifact's name for them through the accepted proposal's target.
        var existing = SeedArtifact("Black Harbor", ArtifactType.Location);
        await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        existing.Visibility = VisibilityScope.GMOnly;

        var after = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        Assert.Multiple(() =>
        {
            Assert.That(after.Value!.MatchedExistingArtifact, Is.False);
            Assert.That(after.Value.EntityId, Is.Not.EqualTo(existing.Id));
        });
    }

    [Test]
    public async Task AStaleFrontRunner_DoesNotBlockTheCandidateBehindIt()
    {
        // Canon holds same-name duplicates, and the exact-case one wins the tie-break. If it goes
        // stale, the right answer is the next candidate — not "no match", which would mint a third
        // copy alongside the survivor.
        var exactCase = SeedArtifact("Black Harbor", ArtifactType.Location);
        var lowercase = SeedArtifact("black harbor", ArtifactType.Location);

        var firstBinding = await ApplyAsync(MakeCreate("Black Harbor", "Location"));
        Assert.That(firstBinding.Value!.EntityId, Is.EqualTo(exactCase.Id), "exact case wins while it is valid");

        exactCase.Status = ArtifactStatus.Archived;

        var after = await ApplyAsync(MakeCreate("Black Harbor", "Location"));

        Assert.Multiple(() =>
        {
            Assert.That(after.Value!.MatchedExistingArtifact, Is.True);
            Assert.That(after.Value.EntityId, Is.EqualTo(lowercase.Id));
            Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(2), "no third copy");
        });
    }

    #endregion

    #region The saving itself

    [Test]
    public async Task ManyCreatesOfOneType_ReadTheCandidateSetOnce()
    {
        for (var i = 0; i < 10; i++)
        {
            await ApplyAsync(MakeCreate($"Waypoint {i}", "Location"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(_artifactRepo.ListByTypeCallCount, Is.EqualTo(1),
                "ten creates of one type used to mean ten full listings");
            Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(10),
                "and all ten still get created — the saving is in the reads, not the writes");
        });
    }

    [Test]
    public async Task ManyCreates_ResolveTheAuthorsVisibilityFilterOnce()
    {
        for (var i = 0; i < 10; i++)
        {
            await ApplyAsync(MakeCreate($"Waypoint {i}", "Location"));
        }

        Assert.That(_memberRepo.GetByWorldAndUserCallCount, Is.EqualTo(1),
            "the author's membership does not change during one accept");
    }

    [Test]
    public async Task CreatesOfDifferentTypes_ReadOneSetEach()
    {
        await ApplyAsync(MakeCreate("Black Harbor", "Location"));
        await ApplyAsync(MakeCreate("Captain Voss", "Character"));
        await ApplyAsync(MakeCreate("Tidewatch", "Location"));
        await ApplyAsync(MakeCreate("Mira Kell", "Character"));

        Assert.That(_artifactRepo.ListByTypeCallCount, Is.EqualTo(2),
            "the set is per type, so two types cost two reads regardless of proposal count");
    }

    #endregion

    private Task<AppResult<ApplyResult>> ApplyAsync(ReviewProposal proposal, Source? source = null) =>
        _applicator.ApplyAsync(proposal, _batch, source ?? _source, VisibilityFilter.ForRole(WorldRole.GM, _gmId),
            CancellationToken.None);

    /// <summary>A second author's source in the same world — one accept spans all of them.</summary>
    private Source SourceBy(Guid authorId) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = _worldId,
        Type = SourceType.SessionNote,
        Title = "Session 9, from the other side of the table",
        Body = "We reached Black Harbor before dark.",
        Visibility = VisibilityScope.PartyVisible,
        ProcessingStatus = SourceProcessingStatus.Processed,
        CreatedByUserId = authorId,
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
    };

    private void SeedMember(Guid userId, WorldRole role) =>
        _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-30)
        }).GetAwaiter().GetResult();

    private Artifact SeedArtifact(string name, ArtifactType type)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private ReviewProposal MakeCreate(string name, string type, string? visibility = null) =>
        MakeProposal(
            ReviewChangeType.CreateArtifact,
            new CreateArtifactPayload(name, type, Summary: null, visibility, Confidence: null));

    private ReviewProposal MakeUpdate(Guid targetId, string? name = null, string? status = null)
    {
        var proposal = MakeProposal(
            ReviewChangeType.UpdateArtifact,
            new UpdateArtifactPayload(name, Summary: null, Visibility: null, Confidence: null, status));
        proposal.TargetId = targetId;
        return proposal;
    }

    private ReviewProposal MakeMerge(Guid targetId, Guid sourceArtifactId)
    {
        var proposal = MakeProposal(
            ReviewChangeType.MergeArtifact,
            new MergeArtifactPayload(sourceArtifactId, Name: null, Summary: null, Visibility: null,
                Confidence: null));
        proposal.TargetId = targetId;
        return proposal;
    }

    private ReviewProposal MakeProposal(ReviewChangeType changeType, object payload) => new()
    {
        Id = Guid.NewGuid(),
        ReviewBatchId = _batch.Id,
        ChangeType = changeType,
        TargetType = ReviewTargetType.Artifact,
        ProposedValueJson = JsonSerializer.Serialize(payload, payload.GetType()),
        Status = ReviewProposalStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
    };
}
