using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LearnedDigestServiceTests
{
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemoryWorldMemberRepository _memberRepo = null!;
    private LearnedDigestService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid PlayerId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _memberRepo = new InMemoryWorldMemberRepository();

        SeedMember(PlayerId, WorldRole.Player);

        _sut = new LearnedDigestService(
            _sourceRepo, _batchRepo, _proposalRepo, _artifactRepo, _factRepo,
            _relationshipRepo, _memberRepo);
    }

    private Task<Nornis.Application.Errors.AppResult<LearnedDigest>> Read(WorldRole role = WorldRole.Player) =>
        _sut.GetAsync(WorldId, PlayerId, role, CancellationToken.None);

    #region Property 3 — nothing hidden is countable

    [Test]
    public async Task HiddenMaterial_ChangesNothingAboutTheView()
    {
        // Correctness Property 3, the one this feature exists to guarantee: a reader must not be
        // able to tell a world with nothing left to disclose from one full of secrets.
        var artifact = SeedArtifact("Captain Voss");
        SeedReveal(Now.AddDays(-1), "The letter names him.", artifact);

        var before = (await Read()).Value!;

        // Arbitrary hidden material, touching nothing that was revealed.
        for (var i = 0; i < 12; i++)
        {
            var secret = SeedArtifact($"Secret {i}", VisibilityScope.GMOnly);
            SeedFact(secret, $"hidden fact {i}", VisibilityScope.GMOnly);
            SeedFact(secret, $"private note {i}", VisibilityScope.Private);
            SeedFact(secret, $"hidden truth {i}", VisibilityScope.PartyVisible, TruthState.Hidden);
        }

        var after = (await Read()).Value!;

        Assert.That(Describe(after), Is.EqualTo(Describe(before)),
            "a well-meant \"and 3 more\" is exactly what this must never grow");
    }

    #endregion

    #region What appears

    [Test]
    public async Task ARevealIsReturnedWithTheGmsOwnWords()
    {
        var artifact = SeedArtifact("Captain Voss");
        SeedReveal(Now.AddDays(-1), "The letter you found names the harbourmaster.", artifact);

        var digest = (await Read()).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(digest.Entries, Has.Count.EqualTo(1));
            Assert.That(digest.Entries[0].GmNote, Is.EqualTo("The letter you found names the harbourmaster."));
            Assert.That(digest.Entries[0].Elements.Select(e => e.Name), Does.Contain("Captain Voss"));
        });
    }

    [Test]
    public async Task TheComposedBodyIsNeverRendered()
    {
        // The body also lists counts of what was promoted, which stops agreeing with the
        // elements the moment one is archived.
        var artifact = SeedArtifact("Captain Voss");
        var reveal = SeedReveal(Now.AddDays(-1), note: null, artifact);
        reveal.Body = "Revealed to the party:\n- Character: Captain Voss\n- 4 fact(s)";

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries[0].GmNote, Is.Null);
    }

    [Test]
    public async Task RevealsComeNewestFirst()
    {
        SeedReveal(Now.AddDays(-9), "older", SeedArtifact("A"));
        SeedReveal(Now.AddDays(-2), "newer", SeedArtifact("B"));

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries.Select(e => e.GmNote), Is.EqualTo(["newer", "older"]).AsCollection);
    }

    [Test]
    public async Task OnlyRevealSourcesAppear()
    {
        var artifact = SeedArtifact("Captain Voss");
        var session = SeedReveal(Now.AddDays(-1), "a session", artifact);
        session.Type = SourceType.SessionNote;

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries, Is.Empty, "phase 1 reports deliberate disclosure, not everything");
    }

    #endregion

    #region What is dropped

    [Test]
    public async Task AnArchivedElementIsOmitted()
    {
        var kept = SeedArtifact("Captain Voss");
        var archived = SeedArtifact("A merged duplicate");
        SeedReveal(Now.AddDays(-1), "both", kept, archived);
        archived.Status = ArtifactStatus.Archived;

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries[0].Elements.Select(e => e.Name), Is.EquivalentTo(["Captain Voss"]));
    }

    [Test]
    public async Task AnEntryWhoseElementsAllVanishedIsDroppedEntirely()
    {
        // "The GM revealed something on the 4th, and it is gone" is the gap that invites the
        // question this view must not provoke.
        var artifact = SeedArtifact("A merged duplicate");
        SeedReveal(Now.AddDays(-1), "gone now", artifact);
        artifact.Status = ArtifactStatus.Archived;

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries, Is.Empty);
    }

    [Test]
    public async Task AFactStillMarkedHiddenIsNotSomethingLearned()
    {
        // Party-visible but Hidden: the party can see the shape of the claim, not its truth.
        var artifact = SeedArtifact("Captain Voss");
        var reveal = SeedReveal(Now.AddDays(-1), "note", artifact);
        var fact = SeedFact(artifact, "true allegiance", VisibilityScope.PartyVisible, TruthState.Hidden);
        AttachProposal(reveal, ReviewTargetType.ArtifactFact, fact.Id);

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries[0].Elements.Any(e => e.Kind == "Fact"), Is.False);
    }

    #endregion

    #region The marker

    [Test]
    public async Task OnlyRevealsAfterTheMarkerAreReturned()
    {
        SeedReveal(Now.AddDays(-9), "already read", SeedArtifact("A"));
        SeedReveal(Now.AddDays(-1), "new", SeedArtifact("B"));
        Member(PlayerId).LearnedSeenAt = Now.AddDays(-5);

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries.Select(e => e.GmNote), Is.EqualTo(["new"]).AsCollection);
    }

    [Test]
    public async Task AMemberWhoHasNeverLookedGetsABoundedFirstView()
    {
        // Correctness Property 5: joining a world with years of disclosures behind it must not
        // hand over all of them.
        for (var i = 0; i < LearnedDigestService.FirstViewLimit + 7; i++)
        {
            SeedReveal(Now.AddDays(-i - 1), $"reveal {i}", SeedArtifact($"Entry {i}"));
        }

        var digest = (await Read()).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(digest.SeenThrough, Is.Null);
            Assert.That(digest.Entries, Has.Count.EqualTo(LearnedDigestService.FirstViewLimit));
            Assert.That(digest.HasMore, Is.True);
        });
    }

    [Test]
    public async Task ReadingDoesNotMarkAnythingSeen()
    {
        SeedReveal(Now.AddDays(-1), "note", SeedArtifact("A"));

        await Read();

        Assert.That(Member(PlayerId).LearnedSeenAt, Is.Null,
            "a reader who is interrupted must not lose the list");
    }

    [Test]
    public async Task MarkSeen_AdvancesOnlyTheCallingMembersMarker()
    {
        // Correctness Property 6.
        SeedMember(GmId, WorldRole.GM);
        var point = Now.AddDays(-1);

        await _sut.MarkSeenAsync(WorldId, PlayerId, point, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(Member(PlayerId).LearnedSeenAt, Is.EqualTo(point));
            Assert.That(Member(GmId).LearnedSeenAt, Is.Null);
        });
    }

    [Test]
    public async Task MarkSeen_ForANonMember_Returns404()
    {
        var result = await _sut.MarkSeenAsync(WorldId, Guid.NewGuid(), Now, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        });
    }

    #endregion

    #region Read-only

    [Test]
    public async Task Reading_ChangesNothing()
    {
        // Correctness Property 1.
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedReveal(Now.AddDays(-1), "note", artifact);

        await Read();

        Assert.Multiple(() =>
        {
            Assert.That(_artifactRepo.Artifacts.Single(a => a.Id == artifact.Id).Visibility,
                Is.EqualTo(VisibilityScope.PartyVisible));
            Assert.That(_artifactRepo.Artifacts.Single(a => a.Id == artifact.Id).Status,
                Is.EqualTo(ArtifactStatus.Active));
        });
    }

    #endregion

    #region The wider delta (phase F)

    [Test]
    public async Task ASessionsRecordedMaterialAppearsAlongsideDisclosures()
    {
        SeedReveal(Now.AddDays(-2), "The letter names him.", SeedArtifact("Captain Voss"));
        SeedSession(Now.AddDays(-1), SeedArtifact("Black Harbor"));

        var digest = (await Read()).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(digest.Entries, Has.Count.EqualTo(2));
            Assert.That(digest.Entries[0].Kind, Is.EqualTo(LearnedEntryKind.Recorded), "newest first");
            Assert.That(digest.Entries[1].Kind, Is.EqualTo(LearnedEntryKind.Disclosed));
        });
    }

    [Test]
    public async Task RecordedMaterialCarriesNoGmNote()
    {
        // A session note is the record catching up, not the GM addressing the party. Rendering
        // its body as a note would put words in their mouth.
        SeedSession(Now.AddDays(-1), SeedArtifact("Black Harbor"));

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries.Single().GmNote, Is.Null);
    }

    [Test]
    public async Task AGmOnlySessionIsInvisibleToThePlayer()
    {
        var session = SeedSession(Now.AddDays(-1), SeedArtifact("A private place"));
        session.Visibility = VisibilityScope.GMOnly;

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries, Is.Empty);
    }

    [Test]
    public async Task ASessionWhoseProposalsWereAllRejectedIsDropped()
    {
        // It recorded nothing, so there is nothing to have learned from it.
        var artifact = SeedArtifact("Rejected thing");
        SeedSession(Now.AddDays(-1), artifact);
        foreach (var p in _proposalRepo.Proposals)
        {
            p.Status = ReviewProposalStatus.Rejected;
        }

        var digest = (await Read()).Value!;

        Assert.That(digest.Entries, Is.Empty);
    }

    [Test]
    public async Task HiddenMaterial_StillChangesNothing_AcrossTheCombinedView()
    {
        // Correctness Property 3, re-asserted now that the view carries both kinds (F3).
        SeedReveal(Now.AddDays(-2), "The letter names him.", SeedArtifact("Captain Voss"));
        SeedSession(Now.AddDays(-1), SeedArtifact("Black Harbor"));

        var before = Describe((await Read()).Value!);

        for (var i = 0; i < 8; i++)
        {
            var secret = SeedArtifact($"Secret {i}", VisibilityScope.GMOnly);
            SeedFact(secret, $"hidden {i}", VisibilityScope.GMOnly);
            SeedSession(Now.AddDays(-1), secret).Visibility = VisibilityScope.GMOnly;

            // A GM-only session over material the party CAN see. Without this the property
            // passes on element filtering alone and never exercises the source gate — which a
            // red-check proved, so it is here on purpose.
            SeedSession(Now.AddDays(-1), SeedArtifact($"Known place {i}")).Visibility =
                VisibilityScope.GMOnly;
        }

        Assert.That(Describe((await Read()).Value!), Is.EqualTo(before));
    }

    #endregion

    #region The unseen count (phase E)

    [Test]
    public async Task CountUnseen_CountsOnlyDisclosures()
    {
        SeedReveal(Now.AddDays(-2), "one", SeedArtifact("A"));
        SeedSession(Now.AddDays(-1), SeedArtifact("B"));

        var count = await _sut.CountUnseenAsync(WorldId, PlayerId, WorldRole.Player, CancellationToken.None);

        // The badge announces that the GM told you something. A session being processed is not
        // an event the party needs pulling back to the app for.
        Assert.That(count.Value, Is.EqualTo(1));
    }

    [Test]
    public async Task CountUnseen_IsZeroOnceMarked()
    {
        SeedReveal(Now.AddDays(-2), "one", SeedArtifact("A"));
        Member(PlayerId).LearnedSeenAt = Now;

        var count = await _sut.CountUnseenAsync(WorldId, PlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(count.Value, Is.Zero);
    }

    [Test]
    public async Task CountUnseen_NeverPromisesMoreThanTheFirstViewWillShow()
    {
        for (var i = 0; i < LearnedDigestService.FirstViewLimit + 6; i++)
        {
            SeedReveal(Now.AddDays(-i - 1), $"r{i}", SeedArtifact($"E{i}"));
        }

        var count = await _sut.CountUnseenAsync(WorldId, PlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(count.Value, Is.EqualTo(LearnedDigestService.FirstViewLimit));
    }

    #endregion

    #region Seeding

    private static string Describe(LearnedDigest digest) =>
        string.Join("|", digest.Entries.Select(e =>
            $"{e.Kind}:{e.SourceId}:{e.GmNote}:{string.Join(",", e.Elements.Select(x => $"{x.Kind}/{x.Name}/{x.Detail}"))}"))
        + $"|hasMore={digest.HasMore}";

    private WorldMember Member(Guid userId) =>
        _memberRepo.GetByWorldAndUserAsync(WorldId, userId, CancellationToken.None).GetAwaiter().GetResult()!;

    private void SeedMember(Guid userId, WorldRole role) =>
        _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            UserId = userId,
            Role = role,
            JoinedAt = Now.AddYears(-1)
        }, CancellationToken.None).GetAwaiter().GetResult();

    private Artifact SeedArtifact(string name, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Name = name,
            Type = ArtifactType.Character,
            Visibility = visibility,
            Status = ArtifactStatus.Active,
            CreatedAt = Now.AddDays(-100),
            UpdatedAt = Now
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private ArtifactFact SeedFact(
        Artifact artifact, string value, VisibilityScope visibility, TruthState truthState = TruthState.Confirmed)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = "note",
            Value = value,
            Visibility = visibility,
            TruthState = truthState,
            CreatedAt = Now.AddDays(-50),
            UpdatedAt = Now
        };
        _factRepo.Seed(fact);
        return fact;
    }

    private Source SeedReveal(DateTimeOffset occurredAt, string? note, params Artifact[] artifacts)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.Reveal,
            Title = $"Reveal — {occurredAt:yyyy-MM-dd}",
            Body = "Revealed to the party:",
            RevealNote = note,
            OccurredAt = occurredAt,
            CreatedAt = occurredAt,
            CreatedByUserId = GmId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        _sourceRepo.CreateAsync(source, CancellationToken.None).GetAwaiter().GetResult();

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Kind = ReviewBatchKinds.Reveal,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = occurredAt
        };
        _batchRepo.CreateAsync(batch, CancellationToken.None).GetAwaiter().GetResult();

        foreach (var artifact in artifacts)
        {
            AttachProposal(source, ReviewTargetType.Artifact, artifact.Id);
        }

        return source;
    }

    /// <summary>An ordinary session note and its extraction batch — the null Kind is what
    /// distinguishes it from every synthetic batch.</summary>
    private Source SeedSession(DateTimeOffset occurredAt, params Artifact[] artifacts)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = $"Session — {occurredAt:yyyy-MM-dd}",
            Body = "We questioned Captain Voss in Black Harbor.",
            OccurredAt = occurredAt,
            CreatedAt = occurredAt,
            CreatedByUserId = GmId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        _sourceRepo.CreateAsync(source, CancellationToken.None).GetAwaiter().GetResult();

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Kind = null,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = occurredAt
        };
        _batchRepo.CreateAsync(batch, CancellationToken.None).GetAwaiter().GetResult();

        foreach (var artifact in artifacts)
        {
            _proposalRepo.CreateAsync(new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = artifact.Id,
                ProposedValueJson = "{}",
                Rationale = "Extracted.",
                Status = ReviewProposalStatus.Accepted,
                CreatedAt = occurredAt
            }, CancellationToken.None).GetAwaiter().GetResult();
        }

        return source;
    }

    private void AttachProposal(Source reveal, ReviewTargetType targetType, Guid targetId)
    {
        var batch = _batchRepo.ListBySourceAsync(reveal.Id, CancellationToken.None).GetAwaiter().GetResult()
            .Single(b => b.Kind == ReviewBatchKinds.Reveal);

        _proposalRepo.CreateAsync(new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.UpdateArtifact,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValueJson = "{}",
            Rationale = "Revealed to the party.",
            Status = ReviewProposalStatus.Accepted,
            CreatedAt = Now
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    #endregion
}
