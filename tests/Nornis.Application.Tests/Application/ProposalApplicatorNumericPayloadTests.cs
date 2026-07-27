using Nornis.Application.Application;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Application;

/// <summary>
/// Defense half of the quoted-number fix: payloads already sitting in the database from
/// before the extraction boundary normalized them — and payloads a reviewer hand-edited —
/// must still apply. Genuinely non-numeric strings still fail.
/// </summary>
[TestFixture]
public class ProposalApplicatorNumericPayloadTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private ProposalApplicator _applicator = null!;

    private Guid _worldId;
    private Source _source = null!;
    private ReviewBatch _batch = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        var sourceRepo = new InMemorySourceRepository();

        _applicator = new ProposalApplicator(
            _artifactRepo, _factRepo, new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceReferenceRepository(), sourceRepo,
            new InMemorySourceAttachmentRepository(), new InMemoryMapPlacemarkRepository(),
            new InMemoryWorldMemberRepository());

        _worldId = Guid.NewGuid();
        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 1",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        sourceRepo.Seed(_source);

        _batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
    }

    [Test]
    public async Task CreateArtifact_StoredQuotedConfidence_AppliesAndStoresTheNumber()
    {
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact,
            """{"name":"Captain Voss","type":"Character","confidence":"0.99"}""");

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_artifactRepo.Artifacts.Single().Confidence, Is.EqualTo(0.99m));
    }

    [Test]
    public async Task AddFact_StoredQuotedConfidence_Applies()
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = _source.CreatedByUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _artifactRepo.Seed(artifact);

        var proposal = MakeProposal(ReviewChangeType.AddFact,
            """{"predicate":"serves","value":"the harbor guard","confidence":"0.75"}""",
            targetId: artifact.Id);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_factRepo.Facts.Single().Confidence, Is.EqualTo(0.75m));
    }

    [Test]
    public async Task CreateArtifact_NonNumericConfidence_FailsWithInvalidPayload()
    {
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact,
            """{"name":"Captain Voss","type":"Character","confidence":"high"}""");

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_payload"));
        Assert.That(_artifactRepo.Artifacts, Is.Empty);
    }

    private ReviewProposal MakeProposal(ReviewChangeType changeType, string json, Guid? targetId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = changeType,
            TargetType = changeType == ReviewChangeType.AddFact
                ? ReviewTargetType.ArtifactFact
                : ReviewTargetType.Artifact,
            TargetId = targetId,
            ProposedValueJson = json,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
}
