using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Tests.Generators;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Properties;

/// <summary>
/// Property-based tests for ReviewService covering batch completion lifecycle,
/// idempotent terminal states, cross-state transitions, visibility defaults,
/// review queue ordering, and pagination (Properties 16-22).
/// Uses FsCheck.NUnit with custom Arbitraries and in-memory fakes.
/// </summary>
[TestFixture]
[Category("Feature: review-proposal-workflow")]
public class ReviewServicePropertyTests4
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region Helpers

    /// <summary>
    /// Creates a ReviewService with FakeProposalValidator and FakeProposalApplicator.
    /// Used for tests where we don't need real validation/application.
    /// </summary>
    private static FakeTestContext CreateFakeService()
    {
        var batchRepo = new InMemoryReviewBatchRepository();
        var proposalRepo = new InMemoryReviewProposalRepository(batchRepo);
        var sourceRepo = new InMemorySourceRepository();
        var artifactRepo = new InMemoryArtifactRepository();
        var artifactFactRepo = new InMemoryArtifactFactRepository();
        var artifactRelationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();
        var unitOfWork = new FakeUnitOfWork();
        var validator = new FakeProposalValidator();
        var applicator = new FakeProposalApplicator();

        var service = new ReviewService(
            proposalRepo,
            batchRepo,
            sourceRepo,
            artifactRepo,
            artifactFactRepo,
            artifactRelationshipRepo,
            sourceRefRepo,
            unitOfWork,
            validator,
            applicator);

        return new FakeTestContext(
            service, proposalRepo, batchRepo, sourceRepo,
            artifactRepo, artifactFactRepo, artifactRelationshipRepo, sourceRefRepo);
    }

    /// <summary>
    /// Creates a ReviewService with REAL ProposalValidator and REAL ProposalApplicator.
    /// Used for Property 20 where visibility defaults are tested through real application.
    /// </summary>
    private static RealTestContext CreateRealService()
    {
        var batchRepo = new InMemoryReviewBatchRepository();
        var proposalRepo = new InMemoryReviewProposalRepository(batchRepo);
        var sourceRepo = new InMemorySourceRepository();
        var artifactRepo = new InMemoryArtifactRepository();
        var artifactFactRepo = new InMemoryArtifactFactRepository();
        var artifactRelationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();
        var unitOfWork = new FakeUnitOfWork();
        var validator = new ProposalValidator();
        var applicator = new ProposalApplicator(
            artifactRepo,
            artifactFactRepo,
            artifactRelationshipRepo,
            sourceRefRepo,
            sourceRepo);

        var service = new ReviewService(
            proposalRepo,
            batchRepo,
            sourceRepo,
            artifactRepo,
            artifactFactRepo,
            artifactRelationshipRepo,
            sourceRefRepo,
            unitOfWork,
            validator,
            applicator);

        return new RealTestContext(
            service, proposalRepo, batchRepo, sourceRepo,
            artifactRepo, artifactFactRepo, artifactRelationshipRepo, sourceRefRepo);
    }

    private record FakeTestContext(
        ReviewService Service,
        InMemoryReviewProposalRepository ProposalRepo,
        InMemoryReviewBatchRepository BatchRepo,
        InMemorySourceRepository SourceRepo,
        InMemoryArtifactRepository ArtifactRepo,
        InMemoryArtifactFactRepository ArtifactFactRepo,
        InMemoryArtifactRelationshipRepository ArtifactRelationshipRepo,
        InMemorySourceReferenceRepository SourceRefRepo);

    private record RealTestContext(
        ReviewService Service,
        InMemoryReviewProposalRepository ProposalRepo,
        InMemoryReviewBatchRepository BatchRepo,
        InMemorySourceRepository SourceRepo,
        InMemoryArtifactRepository ArtifactRepo,
        InMemoryArtifactFactRepository ArtifactFactRepo,
        InMemoryArtifactRelationshipRepository ArtifactRelationshipRepo,
        InMemorySourceReferenceRepository SourceRefRepo);

    #endregion

    #region Property 16: All Proposals Terminal Transitions Batch to Completed

    /// <summary>
    /// Property 16: All Proposals Terminal Transitions Batch to Completed
    ///
    /// Generate ReviewBatch in InReview with all-but-one proposals terminal;
    /// bring last proposal to terminal; assert batch Status=Completed and CompletedAt set.
    ///
    /// **Validates: Requirements 8.2**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 16: All Proposals Terminal Transitions Batch to Completed")]
    public Property All_proposals_terminal_transitions_batch_to_completed(PositiveInt countRaw, bool lastAccepted)
    {
        var totalProposals = (countRaw.Get % 5) + 2; // 2-6 proposals
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        // Batch starts as InReview (some proposals already reviewed)
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create all-but-one proposals in terminal state
        for (var i = 0; i < totalProposals - 1; i++)
        {
            var terminalProposal = new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = null,
                ProposedValueJson = $"{{\"name\":\"Artifact {i}\",\"type\":\"Character\"}}",
                Rationale = $"Proposal {i}",
                Confidence = 0.8m,
                Status = i % 2 == 0 ? ReviewProposalStatus.Accepted : ReviewProposalStatus.Rejected,
                CreatedAt = batch.CreatedAt.AddMinutes(i + 1),
                ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                ReviewedByUserId = userId
            };
            ctx.ProposalRepo.CreateAsync(terminalProposal).GetAwaiter().GetResult();
        }

        // Create the last proposal in Pending state
        var lastProposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Last One\",\"type\":\"Character\"}",
            Rationale = "Last proposal",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(totalProposals)
        };
        ctx.ProposalRepo.CreateAsync(lastProposal).GetAwaiter().GetResult();

        // Bring the last proposal to terminal state
        var before = DateTimeOffset.UtcNow;
        if (lastAccepted)
        {
            var result = ctx.Service.AcceptProposalAsync(
                new AcceptProposalCommand(lastProposal.Id, worldId, userId, WorldRole.GM),
                CancellationToken.None).GetAwaiter().GetResult();
            if (!result.IsSuccess)
                return false.Label($"Accept failed: {result.Error!.Code} - {result.Error!.Message}");
        }
        else
        {
            var result = ctx.Service.RejectProposalAsync(
                new RejectProposalCommand(lastProposal.Id, worldId, userId, WorldRole.GM),
                CancellationToken.None).GetAwaiter().GetResult();
            if (!result.IsSuccess)
                return false.Label($"Reject failed: {result.Error!.Code} - {result.Error!.Message}");
        }

        // Assert batch transitioned to Completed
        var updatedBatch = ctx.BatchRepo.Batches.First(b => b.Id == batch.Id);
        var isCompleted = updatedBatch.Status == ReviewBatchStatus.Completed;
        var completedAtSet = updatedBatch.CompletedAt.HasValue && updatedBatch.CompletedAt.Value >= before;

        return isCompleted.Label($"Batch should be Completed, got {updatedBatch.Status}")
            .And(completedAtSet.Label("CompletedAt should be set to approximately current UTC"));
    }

    #endregion

    #region Property 17: Batch Not Completed While Non-Terminal Proposals Remain

    /// <summary>
    /// Property 17: Batch Not Completed While Non-Terminal Proposals Remain
    ///
    /// Generate batch with some Pending or Edited proposals remaining;
    /// assert batch Status is NOT Completed.
    ///
    /// **Validates: Requirements 8.3**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 17: Batch Not Completed While Non-Terminal Proposals Remain")]
    public Property Batch_not_completed_while_non_terminal_proposals_remain(PositiveInt countRaw)
    {
        var totalProposals = (countRaw.Get % 5) + 3; // 3-7 proposals
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create all proposals as Pending
        var proposalIds = new List<Guid>();
        for (var i = 0; i < totalProposals; i++)
        {
            var proposal = new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = null,
                ProposedValueJson = $"{{\"name\":\"Artifact {i}\",\"type\":\"Character\"}}",
                Rationale = $"Proposal {i}",
                Confidence = 0.8m,
                Status = ReviewProposalStatus.Pending,
                CreatedAt = batch.CreatedAt.AddMinutes(i + 1)
            };
            ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
            proposalIds.Add(proposal.Id);
        }

        // Accept only some proposals (leave at least 2 remaining as Pending)
        var reviewCount = totalProposals / 2; // Review about half
        for (var i = 0; i < reviewCount; i++)
        {
            ctx.Service.AcceptProposalAsync(
                new AcceptProposalCommand(proposalIds[i], worldId, userId, WorldRole.GM),
                CancellationToken.None).GetAwaiter().GetResult();
        }

        // Assert batch is NOT Completed (should be InReview since some are still Pending)
        var updatedBatch = ctx.BatchRepo.Batches.First(b => b.Id == batch.Id);
        var notCompleted = updatedBatch.Status != ReviewBatchStatus.Completed;
        var isInReview = updatedBatch.Status == ReviewBatchStatus.InReview;
        var completedAtNull = !updatedBatch.CompletedAt.HasValue;

        return notCompleted.Label($"Batch should NOT be Completed, got {updatedBatch.Status}")
            .And(isInReview.Label($"Batch should be InReview, got {updatedBatch.Status}"))
            .And(completedAtNull.Label("CompletedAt should not be set"));
    }

    #endregion

    #region Property 18: Idempotent Terminal State

    /// <summary>
    /// Property 18: Idempotent Terminal State
    ///
    /// Accept an already-Accepted proposal; assert success with original ReviewedAt/ReviewedByUserId,
    /// no new entities; reject an already-Rejected proposal; assert success without state changes.
    ///
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 18: Idempotent Accept of Already-Accepted Proposal")]
    public Property Idempotent_accept_of_already_accepted_proposal(ProposalWithContext pwc)
    {
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var originalReviewerId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var originalReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create an already-Accepted proposal
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = Guid.NewGuid(),
            ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
            Rationale = "Already accepted",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Accepted,
            CreatedAt = batch.CreatedAt.AddMinutes(1),
            ReviewedAt = originalReviewedAt,
            ReviewedByUserId = originalReviewerId
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        var sourceRefCountBefore = ctx.SourceRefRepo.References.Count;
        var artifactCountBefore = ctx.ArtifactRepo.Artifacts.Count;

        // Accept the already-accepted proposal again (different user)
        var result = ctx.Service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"Idempotent accept failed: {result.Error!.Code} - {result.Error!.Message}");

        var value = result.Value!;
        var preservedReviewedAt = value.ReviewedAt == originalReviewedAt;
        var preservedReviewedBy = value.ReviewedByUserId == originalReviewerId;
        var noNewArtifacts = ctx.ArtifactRepo.Artifacts.Count == artifactCountBefore;
        var noNewSourceRefs = ctx.SourceRefRepo.References.Count == sourceRefCountBefore;

        return preservedReviewedAt.Label($"ReviewedAt should be original {originalReviewedAt}, got {value.ReviewedAt}")
            .And(preservedReviewedBy.Label($"ReviewedByUserId should be {originalReviewerId}, got {value.ReviewedByUserId}"))
            .And(noNewArtifacts.Label("No new artifacts should be created"))
            .And(noNewSourceRefs.Label("No new source references should be created"));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 18: Idempotent Reject of Already-Rejected Proposal")]
    public Property Idempotent_reject_of_already_rejected_proposal(ProposalWithContext pwc)
    {
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var originalReviewerId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var originalReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create an already-Rejected proposal
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Silver Key\",\"type\":\"Item\"}",
            Rationale = "Already rejected",
            Confidence = 0.7m,
            Status = ReviewProposalStatus.Rejected,
            CreatedAt = batch.CreatedAt.AddMinutes(1),
            ReviewedAt = originalReviewedAt,
            ReviewedByUserId = originalReviewerId
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        // Reject the already-rejected proposal again (different user)
        var result = ctx.Service.RejectProposalAsync(
            new RejectProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"Idempotent reject failed: {result.Error!.Code} - {result.Error!.Message}");

        var value = result.Value!;
        var preservedReviewedAt = value.ReviewedAt == originalReviewedAt;
        var preservedReviewedBy = value.ReviewedByUserId == originalReviewerId;

        // Verify proposal state is unchanged
        var updatedProposal = ctx.ProposalRepo.Proposals.First(p => p.Id == proposal.Id);
        var statusUnchanged = updatedProposal.Status == ReviewProposalStatus.Rejected;
        var reviewedAtUnchanged = updatedProposal.ReviewedAt == originalReviewedAt;

        return preservedReviewedAt.Label($"ReviewedAt should be original {originalReviewedAt}, got {value.ReviewedAt}")
            .And(preservedReviewedBy.Label($"ReviewedByUserId should be {originalReviewerId}, got {value.ReviewedByUserId}"))
            .And(statusUnchanged.Label("Proposal status should remain Rejected"))
            .And(reviewedAtUnchanged.Label("Proposal ReviewedAt should remain unchanged"));
    }

    #endregion

    #region Property 19: Cross-State Terminal Transition Error

    /// <summary>
    /// Property 19: Cross-State Terminal Transition Error
    ///
    /// Accept a Rejected proposal; assert error; reject an Accepted proposal; assert error.
    ///
    /// **Validates: Requirements 10.3**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 19: Accept Rejected Proposal Returns Error")]
    public Property Accept_rejected_proposal_returns_conflict_error(ProposalWithContext pwc)
    {
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create a Rejected proposal
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
            Rationale = "Rejected proposal",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Rejected,
            CreatedAt = batch.CreatedAt.AddMinutes(1),
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReviewedByUserId = Guid.NewGuid()
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        // Try to accept it — should fail with conflict
        var result = ctx.Service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        var isError = !result.IsSuccess;
        var isConflict = result.Error?.StatusCode == 409;
        var codeIsConflict = result.Error?.Code == "conflict";

        return isError.Label("Should return error when accepting a rejected proposal")
            .And(isConflict.Label($"Should be 409, got {result.Error?.StatusCode}"))
            .And(codeIsConflict.Label($"Error code should be 'conflict', got '{result.Error?.Code}'"));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 19: Reject Accepted Proposal Returns Error")]
    public Property Reject_accepted_proposal_returns_conflict_error(ProposalWithContext pwc)
    {
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create an Accepted proposal
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = Guid.NewGuid(),
            ProposedValueJson = "{\"name\":\"Silver Key\",\"type\":\"Item\"}",
            Rationale = "Accepted proposal",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Accepted,
            CreatedAt = batch.CreatedAt.AddMinutes(1),
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReviewedByUserId = Guid.NewGuid()
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        // Try to reject it — should fail with conflict
        var result = ctx.Service.RejectProposalAsync(
            new RejectProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        var isError = !result.IsSuccess;
        var isConflict = result.Error?.StatusCode == 409;
        var codeIsConflict = result.Error?.Code == "conflict";

        return isError.Label("Should return error when rejecting an accepted proposal")
            .And(isConflict.Label($"Should be 409, got {result.Error?.StatusCode}"))
            .And(codeIsConflict.Label($"Error code should be 'conflict', got '{result.Error?.Code}'"));
    }

    #endregion

    #region Property 20: Accepted Entity Visibility Defaults

    /// <summary>
    /// Property 20: Accepted Entity Visibility Defaults
    ///
    /// Generate proposals without visibility in ProposedValueJson; accept; assert entity inherits
    /// source VisibilityScope; generate proposals with explicit visibility; assert entity uses
    /// specified value.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 20: Accepted Entity Inherits Source Visibility When Not Specified")]
    public Property Accepted_entity_inherits_source_visibility_when_not_specified(VisibilityScope sourceVisibility)
    {
        var ctx = CreateRealService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = sourceVisibility,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create a proposal WITHOUT visibility in ProposedValueJson
        var payloadWithoutVisibility = JsonSerializer.Serialize(new
        {
            name = "Captain Voss",
            type = "Character",
            summary = "A harbor captain"
        }, JsonOptions);

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = payloadWithoutVisibility,
            Rationale = "No visibility specified",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        var result = ctx.Service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"Accept failed: {result.Error!.Code} - {result.Error!.Message}");

        // Assert entity inherits source visibility
        var artifact = ctx.ArtifactRepo.Artifacts.FirstOrDefault();
        if (artifact is null)
            return false.Label("No artifact created");

        var inheritsSourceVisibility = artifact.Visibility == sourceVisibility;

        return inheritsSourceVisibility.Label(
            $"Artifact should inherit source visibility {sourceVisibility}, got {artifact.Visibility}");
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 20: Accepted Entity Uses Explicit Visibility When Specified")]
    public Property Accepted_entity_uses_explicit_visibility_when_specified(
        VisibilityScope sourceVisibility, VisibilityScope explicitVisibility)
    {
        var ctx = CreateRealService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = sourceVisibility,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create a proposal WITH explicit visibility in ProposedValueJson
        var payloadWithVisibility = JsonSerializer.Serialize(new
        {
            name = "Silver Key",
            type = "Item",
            summary = "A mysterious key",
            visibility = explicitVisibility.ToString()
        }, JsonOptions);

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = payloadWithVisibility,
            Rationale = "Explicit visibility",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        var result = ctx.Service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"Accept failed: {result.Error!.Code} - {result.Error!.Message}");

        // Assert entity uses explicit visibility
        var artifact = ctx.ArtifactRepo.Artifacts.FirstOrDefault();
        if (artifact is null)
            return false.Label("No artifact created");

        var usesExplicitVisibility = artifact.Visibility == explicitVisibility;

        return usesExplicitVisibility.Label(
            $"Artifact should use explicit visibility {explicitVisibility}, got {artifact.Visibility}");
    }

    #endregion

    #region Property 21: Review Queue Ordering

    /// <summary>
    /// Property 21: Review Queue Ordering
    ///
    /// Generate proposals with random timestamps across multiple batches; list queue;
    /// assert proposals ordered by CreatedAt ascending within each batch,
    /// batches ordered by CreatedAt ascending.
    ///
    /// **Validates: Requirements 1.8**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 21: Review Queue Ordering")]
    public Property Review_queue_orders_by_batch_then_proposal_created_at(PositiveInt batchCountRaw, PositiveInt proposalsPerBatchRaw)
    {
        var batchCount = (batchCountRaw.Get % 3) + 2; // 2-4 batches
        var proposalsPerBatch = (proposalsPerBatchRaw.Get % 4) + 2; // 2-5 proposals per batch
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        ctx.SourceRepo.Seed(source);

        // Create batches with different CreatedAt timestamps (not in order)
        var batches = new List<ReviewBatch>();
        for (var i = 0; i < batchCount; i++)
        {
            var batch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                SourceId = source.Id,
                Status = ReviewBatchStatus.InReview,
                // Reverse order to test sorting: batch 0 has latest timestamp
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-(batchCount - i))
            };
            batches.Add(batch);
            ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();
        }

        // Create proposals with varying timestamps within each batch
        var allProposalIds = new List<Guid>();
        foreach (var batch in batches)
        {
            for (var j = 0; j < proposalsPerBatch; j++)
            {
                var proposal = new ReviewProposal
                {
                    Id = Guid.NewGuid(),
                    ReviewBatchId = batch.Id,
                    ChangeType = ReviewChangeType.CreateArtifact,
                    TargetType = ReviewTargetType.Artifact,
                    TargetId = null,
                    ProposedValueJson = $"{{\"name\":\"Artifact {j}\",\"type\":\"Character\"}}",
                    Rationale = $"Batch proposal {j}",
                    Confidence = 0.8m,
                    Status = ReviewProposalStatus.Pending,
                    // Reverse order within batch to test sorting
                    CreatedAt = batch.CreatedAt.AddMinutes(proposalsPerBatch - j)
                };
                ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
                allProposalIds.Add(proposal.Id);
            }
        }

        // List review queue
        var result = ctx.Service.ListReviewQueueAsync(
            new ReviewQueueQuery(worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"ListReviewQueue failed: {result.Error!.Code}");

        var proposals = result.Value!.Proposals;

        if (proposals.Count == 0)
            return false.Label("No proposals returned");

        // Verify ordering: batches by CreatedAt ascending, proposals within batch by CreatedAt ascending
        var orderedCorrectly = true;
        for (var i = 1; i < proposals.Count; i++)
        {
            var prev = proposals[i - 1];
            var curr = proposals[i];

            var prevBatch = batches.First(b => b.Id == prev.ReviewBatchId);
            var currBatch = batches.First(b => b.Id == curr.ReviewBatchId);

            if (prevBatch.CreatedAt > currBatch.CreatedAt)
            {
                orderedCorrectly = false;
                break;
            }

            if (prevBatch.Id == currBatch.Id && prev.CreatedAt > curr.CreatedAt)
            {
                orderedCorrectly = false;
                break;
            }
        }

        return orderedCorrectly.Label("Proposals should be ordered by batch CreatedAt then proposal CreatedAt ascending");
    }

    #endregion

    #region Property 22: Review Queue Pagination

    /// <summary>
    /// Property 22: Review Queue Pagination
    ///
    /// Generate >200 matching proposals; list queue; assert exactly 200 returned with
    /// HasMore=true; generate ≤200; assert HasMore=false.
    ///
    /// **Validates: Requirements 1.11**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 5)]
    [Description("Feature: review-proposal-workflow, Property 22: Review Queue Pagination Over 200")]
    public Property Review_queue_pagination_over_200_returns_hasmore_true(PositiveInt extraRaw)
    {
        var extraCount = (extraRaw.Get % 10) + 1; // 1-10 extra beyond 200
        var totalCount = 200 + extraCount;
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create more than 200 proposals
        for (var i = 0; i < totalCount; i++)
        {
            var proposal = new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = null,
                ProposedValueJson = $"{{\"name\":\"Artifact {i}\",\"type\":\"Character\"}}",
                Rationale = $"Proposal {i}",
                Confidence = 0.8m,
                Status = ReviewProposalStatus.Pending,
                CreatedAt = batch.CreatedAt.AddMinutes(i + 1)
            };
            ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
        }

        // List review queue
        var result = ctx.Service.ListReviewQueueAsync(
            new ReviewQueueQuery(worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"ListReviewQueue failed: {result.Error!.Code}");

        var queueResult = result.Value!;
        var returns200 = queueResult.Proposals.Count == 200;
        var hasMoreTrue = queueResult.HasMore;

        return returns200.Label($"Should return exactly 200 proposals, got {queueResult.Proposals.Count}")
            .And(hasMoreTrue.Label("HasMore should be true when >200 proposals exist"));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 22: Review Queue Pagination Under 200")]
    public Property Review_queue_pagination_under_200_returns_hasmore_false(PositiveInt countRaw)
    {
        var count = (countRaw.Get % 50) + 1; // 1-50 proposals (well under 200)
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create fewer than 200 proposals
        for (var i = 0; i < count; i++)
        {
            var proposal = new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = null,
                ProposedValueJson = $"{{\"name\":\"Artifact {i}\",\"type\":\"Character\"}}",
                Rationale = $"Proposal {i}",
                Confidence = 0.8m,
                Status = ReviewProposalStatus.Pending,
                CreatedAt = batch.CreatedAt.AddMinutes(i + 1)
            };
            ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
        }

        // List review queue
        var result = ctx.Service.ListReviewQueueAsync(
            new ReviewQueueQuery(worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"ListReviewQueue failed: {result.Error!.Code}");

        var queueResult = result.Value!;
        var returnsAll = queueResult.Proposals.Count == count;
        var hasMoreFalse = !queueResult.HasMore;

        return returnsAll.Label($"Should return all {count} proposals, got {queueResult.Proposals.Count}")
            .And(hasMoreFalse.Label("HasMore should be false when ≤200 proposals exist"));
    }

    #endregion
}
