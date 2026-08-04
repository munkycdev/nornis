using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class SyntheticBatchWriterTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryReviewBatchRepository _batchRepository = null!;
    private InMemoryReviewProposalRepository _proposalRepository = null!;
    private InMemorySourceReferenceRepository _referenceRepository = null!;
    private FakeProposalApplicator _applicator = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private SyntheticBatchWriter _writer = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _batchRepository = new InMemoryReviewBatchRepository();
        _proposalRepository = new InMemoryReviewProposalRepository();
        _referenceRepository = new InMemorySourceReferenceRepository();
        _applicator = new FakeProposalApplicator();
        _unitOfWork = new FakeUnitOfWork();
        _writer = new SyntheticBatchWriter(
            _sourceRepository, _batchRepository, _proposalRepository,
            _referenceRepository, _applicator, _unitOfWork);
    }

    private static SyntheticSourceSpec SourceSpec() => new()
    {
        WorldId = WorldId,
        ActingUserId = GmId,
        Title = "Artifact merge — Voss → Captain Voss",
        Body = "GM merged duplicate artifact."
    };

    private static SyntheticProposalSpec ProposalSpec(string? notes = null, string? quote = null) => new()
    {
        ChangeType = ReviewChangeType.UpdateArtifact,
        TargetType = ReviewTargetType.Artifact,
        TargetId = Guid.NewGuid(),
        ProposedValueJson = """{"status":"Resolved"}""",
        Rationale = "GM closed this storyline.",
        Confidence = 0.9m,
        ReferenceNotes = notes,
        ReferenceQuote = quote
    };

    #region Accepted shape

    [Test]
    public async Task WriteAccepted_MintsSourceBatchAndAcceptedProposals_InOneCommittedTransaction()
    {
        var result = await _writer.WriteAcceptedAsync(
            SourceSpec(), ReviewBatchKinds.ArtifactMerge, [ProposalSpec(), ProposalSpec()], CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);

        var source = _sourceRepository.Sources.Single();
        Assert.That(source.WorldId, Is.EqualTo(WorldId));
        Assert.That(source.CreatedByUserId, Is.EqualTo(GmId));
        Assert.That(source.Type, Is.EqualTo(SourceType.GMNote), "GMNote is the default synthetic source type");
        Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.GMOnly), "GMOnly is the default synthetic visibility");
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed),
            "a synthetic source records something that already happened and must never be extracted");

        var batch = _batchRepository.Batches.Single();
        Assert.That(batch.Kind, Is.EqualTo(ReviewBatchKinds.ArtifactMerge));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Completed));
        Assert.That(batch.CompletedAt, Is.Not.Null);
        Assert.That(batch.SourceId, Is.EqualTo(source.Id));
        Assert.That(batch.WorldId, Is.EqualTo(WorldId));
        Assert.That(result.Value!.BatchId, Is.EqualTo(batch.Id));
        Assert.That(result.Value.SourceId, Is.EqualTo(source.Id));

        Assert.That(_proposalRepository.Proposals, Has.Count.EqualTo(2));
        foreach (var proposal in _proposalRepository.Proposals)
        {
            Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
            Assert.That(proposal.ReviewedByUserId, Is.EqualTo(GmId));
            Assert.That(proposal.ReviewedAt, Is.Not.Null);
        }

        Assert.That(_applicator.AppliedProposalIds, Has.Count.EqualTo(2), "every proposal goes through the real applicator");
        Assert.That(_applicator.LastActingFilter, Is.EqualTo(VisibilityFilter.All),
            "accepted-shape callers are GM-gated, so resolution is unrestricted");
        Assert.That(_referenceRepository.References, Is.Empty,
            "an accepted batch records no references — the accepted proposal is the record");
        Assert.That(_unitOfWork.Transactions.Single().Committed, Is.True);
    }

    [Test]
    public async Task WriteAccepted_ApplyFailure_RollsBackAndReturnsTheApplicatorsError()
    {
        _applicator.ConfigureFailure("target_not_found", "The artifact is gone.");

        var result = await _writer.WriteAcceptedAsync(
            SourceSpec(), ReviewBatchKinds.ArtifactMerge, [ProposalSpec()], CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("target_not_found"));

        var transaction = _unitOfWork.Transactions.Single();
        Assert.That(transaction.RolledBack, Is.True, "a failed apply must undo the source and batch too");
        Assert.That(transaction.Committed, Is.False);
        Assert.That(_proposalRepository.Proposals.Single().Status, Is.EqualTo(ReviewProposalStatus.Pending),
            "the failed proposal is never accept-stamped");
    }

    [Test]
    public async Task WriteAccepted_HonorsRevealShapedSourceOverrides()
    {
        var result = await _writer.WriteAcceptedAsync(
            new SyntheticSourceSpec
            {
                WorldId = WorldId,
                ActingUserId = GmId,
                Title = "Reveal — 2026-08-04",
                Body = "Revealed to the party.",
                Type = SourceType.Reveal,
                Visibility = VisibilityScope.PartyVisible
            },
            ReviewBatchKinds.Reveal, [ProposalSpec()], CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var source = _sourceRepository.Sources.Single();
        Assert.That(source.Type, Is.EqualTo(SourceType.Reveal));
        Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.PartyVisible),
            "the reveal's synthetic source is the one the party can see");
    }

    #endregion

    #region Pending shape

    [Test]
    public async Task WritePending_MintsPendingBatchWithOneReferencePerProposal_AndNeverApplies()
    {
        var written = await _writer.WritePendingAsync(
            SourceSpec(), ReviewBatchKinds.ContinuityFix,
            [ProposalSpec(notes: "Drafted fix for finding: timeline clash")], CancellationToken.None);

        var batch = _batchRepository.Batches.Single();
        Assert.That(batch.Id, Is.EqualTo(written.BatchId));
        Assert.That(batch.Kind, Is.EqualTo(ReviewBatchKinds.ContinuityFix));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));
        Assert.That(batch.CompletedAt, Is.Null);

        var proposal = _proposalRepository.Proposals.Single();
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Pending), "the GM decides in the review queue");
        Assert.That(proposal.Confidence, Is.EqualTo(0.9m));

        var reference = _referenceRepository.References.Single();
        Assert.That(reference.TargetType, Is.EqualTo(SourceReferenceTargetType.ReviewProposal));
        Assert.That(reference.TargetId, Is.EqualTo(proposal.Id));
        Assert.That(reference.SourceId, Is.EqualTo(written.SourceId));
        Assert.That(reference.Notes, Is.EqualTo("Drafted fix for finding: timeline clash"));

        Assert.That(_applicator.AppliedProposalIds, Is.Empty);
        Assert.That(_unitOfWork.Transactions.Single().Committed, Is.True);
    }

    #endregion

    #region Sweep shape

    private static Source RealSource() => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Type = SourceType.SessionNote,
        Title = "Session 5",
        Body = "Captain Voss returned to Black Harbor.",
        Visibility = VisibilityScope.PartyVisible,
        ProcessingStatus = SourceProcessingStatus.Processed,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        CreatedByUserId = GmId
    };

    [Test]
    public async Task WriteSweep_ZeroProposals_WritesTheCompletedMarkerBatch()
    {
        var source = RealSource();
        _sourceRepository.Seed(source);

        var batchId = await _writer.WriteSweepAsync(
            source, ReviewBatchKinds.RelationshipBackfill, [], CancellationToken.None);

        var batch = _batchRepository.Batches.Single();
        Assert.That(batch.Id, Is.EqualTo(batchId));
        Assert.That(batch.SourceId, Is.EqualTo(source.Id), "the marker rides the real source, not a synthetic one");
        Assert.That(batch.Kind, Is.EqualTo(ReviewBatchKinds.RelationshipBackfill));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Completed));
        Assert.That(batch.CompletedAt, Is.Not.Null);
        Assert.That(_sourceRepository.Sources, Has.Count.EqualTo(1), "a sweep never mints a source");
        Assert.That(_unitOfWork.Transactions, Is.Empty, "a single insert needs no transaction");
    }

    [Test]
    public async Task WriteSweep_WithProposals_IsThePendingShapeOverTheRealSource()
    {
        var source = RealSource();
        _sourceRepository.Seed(source);

        var batchId = await _writer.WriteSweepAsync(
            source, ReviewBatchKinds.RelationshipBackfill,
            [ProposalSpec(quote: "Voss and the caravan")], CancellationToken.None);

        var batch = _batchRepository.Batches.Single();
        Assert.That(batch.Id, Is.EqualTo(batchId));
        Assert.That(batch.SourceId, Is.EqualTo(source.Id));
        Assert.That(batch.WorldId, Is.EqualTo(source.WorldId),
            "the batch files under the source's true world, not a caller-supplied one");
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));

        var reference = _referenceRepository.References.Single();
        Assert.That(reference.SourceId, Is.EqualTo(source.Id));
        Assert.That(reference.Quote, Is.EqualTo("Voss and the caravan"));
        Assert.That(_sourceRepository.Sources, Has.Count.EqualTo(1));
        Assert.That(_unitOfWork.Transactions.Single().Committed, Is.True);
    }

    #endregion

    #region Guards

    [Test]
    public void EveryVerb_RejectsAMissingKind_BecauseNullKindMeansExtraction()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _writer.WriteAcceptedAsync(
            SourceSpec(), " ", [ProposalSpec()], CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(() => _writer.WritePendingAsync(
            SourceSpec(), "", [ProposalSpec()], CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(() => _writer.WriteSweepAsync(
            RealSource(), " ", [], CancellationToken.None));
    }

    [Test]
    public void SyntheticShapes_RejectEmptyProposals_ASourceRecordingNothingIsABug()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _writer.WriteAcceptedAsync(
            SourceSpec(), ReviewBatchKinds.ArtifactMerge, [], CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(() => _writer.WritePendingAsync(
            SourceSpec(), ReviewBatchKinds.ContinuityFix, [], CancellationToken.None));
    }

    #endregion
}
