using System.Text.Json;
using Nornis.Application.Application;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Batch accept used to be order-dependent: it applied the caller's ids in sequence, so a
/// fact selected above the CreateArtifact it references by name failed name resolution and
/// stayed pending. Creates now go first within the selection.
///
/// What batch accept must NOT do is cascade: single accept pulls in prerequisite creates the
/// reviewer did not choose, batch accept applies exactly the chosen set.
/// </summary>
[TestFixture]
public class ReviewServiceBatchOrderingTests
{
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private ReviewService _service = null!;

    private Guid _worldId;
    private Guid _gmUserId;
    private Source _source = null!;
    private ReviewBatch _batch = null!;

    [SetUp]
    public void SetUp()
    {
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _sourceRepo = new InMemorySourceRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        var relationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();

        // Real validator and applicator: the whole point is that name resolution really runs.
        var applicator = new ProposalApplicator(
            _artifactRepo, _factRepo, relationshipRepo, sourceRefRepo, _sourceRepo,
            new InMemorySourceAttachmentRepository(), new InMemoryMapPlacemarkRepository(),
            new InMemoryWorldMemberRepository());

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 1: Black Harbor",
            Body = "We questioned Captain Voss.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmUserId,
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
        _batchRepo.CreateAsync(_batch).GetAwaiter().GetResult();

        _service = new ReviewService(
            _proposalRepo, _batchRepo, _sourceRepo, _artifactRepo, _factRepo,
            relationshipRepo, sourceRefRepo, new FakeUnitOfWork(),
            new ProposalValidator(), applicator);
    }

    [Test]
    public async Task FactBeforeItsCreate_BothSucceed()
    {
        var fact = await SeedAsync(MakeAddFactByName("Captain Voss", "serves", "the harbor guard"));
        var create = await SeedAsync(MakeCreate("Captain Voss", "Character"));

        // Submitted fact-first, which is exactly the order that used to half-fail.
        var result = await _service.BatchAcceptAsync(
            new BatchAcceptCommand([fact.Id, create.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Failed, Is.Empty);
        Assert.That(result.Value.Succeeded, Is.EquivalentTo(new[] { fact.Id, create.Id }));
        Assert.That(fact.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(create.Status, Is.EqualTo(ReviewProposalStatus.Accepted));

        var artifact = _artifactRepo.Artifacts.Single();
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(artifact.Id),
            "the fact must attach to the artifact its sibling create made");
    }

    [Test]
    public async Task RelationshipBeforeBothCreates_AllSucceed()
    {
        var relationship = await SeedAsync(MakeAddRelationshipByName("Captain Voss", "Black Harbor"));
        var createA = await SeedAsync(MakeCreate("Captain Voss", "Character"));
        var createB = await SeedAsync(MakeCreate("Black Harbor", "Location"));

        var result = await _service.BatchAcceptAsync(
            new BatchAcceptCommand([relationship.Id, createA.Id, createB.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Failed, Is.Empty);
        Assert.That(result.Value.Succeeded, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task FactReferencingAnUnselectedCreate_StillFails()
    {
        var fact = await SeedAsync(MakeAddFactByName("Captain Voss", "serves", "the harbor guard"));
        var unselectedCreate = await SeedAsync(MakeCreate("Captain Voss", "Character"));

        var result = await _service.BatchAcceptAsync(
            new BatchAcceptCommand([fact.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Succeeded, Is.Empty);
        Assert.That(result.Value.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("artifact_name_not_found"));
        Assert.That(unselectedCreate.Status, Is.EqualTo(ReviewProposalStatus.Pending),
            "batch accept must never apply a proposal the user did not select");
        Assert.That(_artifactRepo.Artifacts, Is.Empty);
        Assert.That(_factRepo.Facts, Is.Empty);
    }

    [Test]
    public async Task SloppyWhitespaceName_DedupBindsAndTheFactStillResolves()
    {
        // Canon already holds "Salt Factor"; the batch proposes a create spelled with a double
        // space plus a fact referencing the same sloppy spelling.
        //
        // This used to strand permanently: dedup collapses whitespace so the create bound to
        // canon (no insert, proposal Accepted), while name resolution was whitespace-exact so
        // the fact failed artifact_name_not_found — and neither the retry pass nor the
        // prerequisite cascade could help, because the create it needed was already Accepted.
        // Both sides now share ArtifactNameKey.
        var canon = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Storyline,
            Name = "Salt Factor",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        };
        _artifactRepo.Seed(canon);

        var create = await SeedAsync(MakeCreate("Salt  Factor", "Storyline"));
        var fact = await SeedAsync(MakeAddFactByName("Salt  Factor", "involves", "the harbor guild"));

        var result = await _service.BatchAcceptAsync(
            new BatchAcceptCommand([create.Id, fact.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Failed, Is.Empty);
        Assert.That(result.Value.Succeeded, Is.EquivalentTo(new[] { create.Id, fact.Id }));

        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(1), "the create dedup-bound to canon");
        Assert.That(create.AppliedToExistingArtifact, Is.True);
        Assert.That(create.TargetId, Is.EqualTo(canon.Id));
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(canon.Id),
            "the fact must land on the same artifact the create bound to");
    }

    [Test]
    public async Task SloppyWhitespaceInCanon_ResolvesFromACleanReference()
    {
        // The mirror case: canon was stored sloppily, the payload is clean.
        var canon = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Storyline,
            Name = "Salt  Factor",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        };
        _artifactRepo.Seed(canon);

        var fact = await SeedAsync(MakeAddFactByName("Salt Factor", "involves", "the harbor guild"));

        var result = await _service.BatchAcceptAsync(
            new BatchAcceptCommand([fact.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Failed, Is.Empty);
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(canon.Id));
    }

    [Test]
    public async Task EditedProposals_AreAcceptableInABatch()
    {
        var create = await SeedAsync(MakeCreate("Captain Voss", "Character"));
        create.Status = ReviewProposalStatus.Edited;
        create.ReviewedByUserId = _gmUserId;
        create.ReviewedAt = DateTimeOffset.UtcNow;

        var result = await _service.BatchAcceptAsync(
            new BatchAcceptCommand([create.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Failed, Is.Empty);
        Assert.That(create.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
    }

    #region Retry pass

    [Test]
    public async Task RetryPass_RecoversANameFailureExactlyOnce()
    {
        var applicator = new FakeProposalApplicator();
        applicator.ConfigureFailFirstAttemptPerProposal(404, "artifact_name_not_found", "Not yet.");
        var service = BuildServiceWith(applicator);

        var proposal = await SeedAsync(MakeCreate("Captain Voss", "Character"));

        var result = await service.BatchAcceptAsync(
            new BatchAcceptCommand([proposal.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Succeeded, Is.EqualTo(new[] { proposal.Id }));
        Assert.That(result.Value.Failed, Is.Empty, "the recovered proposal must leave the failure list");
        Assert.That(applicator.AppliedProposalIds, Has.Count.EqualTo(2),
            "one ordered pass plus one retry — never more");
    }

    [Test]
    public async Task RetryPass_DoesNotReapplyProposalsThatAlreadySucceeded()
    {
        var applicator = new FakeProposalApplicator();
        applicator.ConfigureFailFirstAttemptPerProposal(404, "artifact_name_not_found", "Not yet.");
        var service = BuildServiceWith(applicator);

        var first = await SeedAsync(MakeCreate("Captain Voss", "Character"));
        var second = await SeedAsync(MakeCreate("Black Harbor", "Location"));

        var result = await service.BatchAcceptAsync(
            new BatchAcceptCommand([first.Id, second.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Succeeded, Is.EquivalentTo(new[] { first.Id, second.Id }));
        Assert.That(result.Value.Succeeded, Is.Unique, "a retried proposal must not be counted twice");
        Assert.That(applicator.AppliedProposalIds.Count(id => id == first.Id), Is.EqualTo(2));
        Assert.That(applicator.AppliedProposalIds.Count(id => id == second.Id), Is.EqualTo(2));
    }

    [Test]
    public async Task RetryPass_LeavesNonNameFailuresAlone()
    {
        var applicator = new FakeProposalApplicator();
        applicator.ConfigureFailure("invalid_payload", "Nope.");
        var service = BuildServiceWith(applicator);

        var proposal = await SeedAsync(MakeCreate("Captain Voss", "Character"));

        var result = await service.BatchAcceptAsync(
            new BatchAcceptCommand([proposal.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("invalid_payload"));
        Assert.That(applicator.AppliedProposalIds, Has.Count.EqualTo(1), "no retry for other errors");
    }

    [Test]
    public async Task StillUnresolvable_RetriesOnceAndKeepsTheOriginalError()
    {
        var applicator = new FakeProposalApplicator();
        applicator.ConfigureFailure("artifact_name_not_found", "No such artifact.");
        var service = BuildServiceWith(applicator);

        var proposal = await SeedAsync(MakeAddFactByName("Ghost", "is", "absent"));

        var result = await service.BatchAcceptAsync(
            new BatchAcceptCommand([proposal.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Value!.Succeeded, Is.Empty);
        Assert.That(result.Value.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("artifact_name_not_found"));
        Assert.That(applicator.AppliedProposalIds, Has.Count.EqualTo(2), "exactly one retry, then stop");
    }

    #endregion

    [Test]
    public async Task CreatesRunFirst_EvenWhenTheCallerInterleavesThem()
    {
        var applicator = new FakeProposalApplicator();
        var service = BuildServiceWith(applicator);

        var factA = await SeedAsync(MakeAddFactByName("A", "p", "v"));
        var createA = await SeedAsync(MakeCreate("A", "Character"));
        var factB = await SeedAsync(MakeAddFactByName("B", "p", "v"));
        var createB = await SeedAsync(MakeCreate("B", "Location"));

        await service.BatchAcceptAsync(
            new BatchAcceptCommand([factA.Id, createA.Id, factB.Id, createB.Id], _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

        Assert.That(applicator.AppliedProposalIds,
            Is.EqualTo(new[] { createA.Id, createB.Id, factA.Id, factB.Id }),
            "creates first, and the caller's relative order preserved inside each group");
    }

    private ReviewService BuildServiceWith(IProposalApplicator applicator) =>
        new(_proposalRepo, _batchRepo, _sourceRepo, _artifactRepo, _factRepo,
            new InMemoryArtifactRelationshipRepository(), new InMemorySourceReferenceRepository(),
            new FakeUnitOfWork(), new ProposalValidator(), applicator);

    private async Task<ReviewProposal> SeedAsync(ReviewProposal proposal)
    {
        await _proposalRepo.CreateAsync(proposal);
        return proposal;
    }

    private ReviewProposal MakeCreate(string name, string type) =>
        MakeProposal(ReviewChangeType.CreateArtifact, ReviewTargetType.Artifact,
            JsonSerializer.Serialize(new CreateArtifactPayload(name, type, null, "PartyVisible", 0.8m)));

    private ReviewProposal MakeAddFactByName(string artifactName, string predicate, string value) =>
        MakeProposal(ReviewChangeType.AddFact, ReviewTargetType.ArtifactFact,
            $$"""{"artifactName":"{{artifactName}}","predicate":"{{predicate}}","value":"{{value}}","confidence":0.8}""");

    private ReviewProposal MakeAddRelationshipByName(string a, string b) =>
        MakeProposal(ReviewChangeType.AddRelationship, ReviewTargetType.ArtifactRelationship,
            $$"""{"artifactAName":"{{a}}","artifactBName":"{{b}}","type":"LocatedIn","confidence":0.8}""");

    private ReviewProposal MakeProposal(ReviewChangeType changeType, ReviewTargetType targetType, string json) =>
        new()
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = changeType,
            TargetType = targetType,
            ProposedValueJson = json,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
}
