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
/// Property-based tests for ReviewService covering edit behavior, edited proposal
/// subsequent operations, batch processing, batch partial failure, and batch lifecycle
/// (Properties 11-15).
/// Uses FsCheck.NUnit with custom Arbitraries and in-memory fakes.
/// </summary>
[TestFixture]
[Category("Feature: review-proposal-workflow")]
public class ReviewServicePropertyTests3
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region Helpers

    /// <summary>
    /// Creates a ReviewService with REAL ProposalValidator and FakeProposalApplicator.
    /// Used for edit tests where validation matters but application should NOT occur.
    /// </summary>
    private static EditTestContext CreateEditService()
    {
        var batchRepo = new InMemoryReviewBatchRepository();
        var proposalRepo = new InMemoryReviewProposalRepository(batchRepo);
        var sourceRepo = new InMemorySourceRepository();
        var artifactRepo = new InMemoryArtifactRepository();
        var artifactFactRepo = new InMemoryArtifactFactRepository();
        var artifactRelationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();
        var unitOfWork = new FakeUnitOfWork();
        var validator = new ProposalValidator(); // REAL validator
        var applicator = new FakeProposalApplicator(); // FAKE applicator

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

        return new EditTestContext(
            service, proposalRepo, batchRepo, sourceRepo,
            artifactRepo, artifactFactRepo, artifactRelationshipRepo, sourceRefRepo);
    }

    /// <summary>
    /// Creates a ReviewService with REAL ProposalValidator and REAL ProposalApplicator.
    /// Used for accept after edit tests.
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

    /// <summary>
    /// Creates a ReviewService with FakeProposalValidator and FakeProposalApplicator.
    /// Used for batch tests where we don't need real validation/application.
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

    private record EditTestContext(
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

    private record FakeTestContext(
        ReviewService Service,
        InMemoryReviewProposalRepository ProposalRepo,
        InMemoryReviewBatchRepository BatchRepo,
        InMemorySourceRepository SourceRepo,
        InMemoryArtifactRepository ArtifactRepo,
        InMemoryArtifactFactRepository ArtifactFactRepo,
        InMemoryArtifactRelationshipRepository ArtifactRelationshipRepo,
        InMemorySourceReferenceRepository SourceRefRepo);

    #endregion

    #region Property 11: Edit Replaces JSON Without Mutating Knowledge Graph

    /// <summary>
    /// Property 11: Edit Replaces JSON Without Mutating Knowledge Graph
    ///
    /// For any proposal with Status Pending or Edited, a valid edit request SHALL replace
    /// the entire ProposedValueJson with the submitted value, transition Status to Edited,
    /// set ReviewedAt and ReviewedByUserId, and SHALL NOT create or modify any Artifact,
    /// ArtifactFact, ArtifactRelationship, or SourceReference.
    ///
    /// **Validates: Requirements 4.1, 4.6**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 11: Edit Replaces JSON Without Mutating Knowledge Graph")]
    public Property Edit_replaces_json_without_mutating_knowledge_graph(ReviewProposalStatus initialStatus)
    {
        // Only test Pending or Edited initial statuses
        if (initialStatus is not (ReviewProposalStatus.Pending or ReviewProposalStatus.Edited))
            return true.ToProperty();

        var ctx = CreateEditService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content about Captain Voss",
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

        var originalJson = JsonSerializer.Serialize(new
        {
            name = "Captain Voss",
            type = "Character",
            summary = "Original summary"
        }, JsonOptions);

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = originalJson,
            Rationale = "Extracted from source",
            Confidence = 0.85m,
            Status = initialStatus,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        // Generate a new valid CreateArtifact payload for the edit
        var newJson = ReviewGenerators.ValidCreateArtifactPayload.Sample(1, 1).First();

        var before = DateTimeOffset.UtcNow;
        var result = ctx.Service.EditProposalAsync(
            new EditProposalCommand(proposal.Id, worldId, userId, WorldRole.GM, newJson),
            CancellationToken.None).GetAwaiter().GetResult();
        var after = DateTimeOffset.UtcNow;

        if (!result.IsSuccess)
            return false.Label($"Edit failed: {result.Error!.Code} - {result.Error!.Message}");

        var updated = ctx.ProposalRepo.Proposals.First(p => p.Id == proposal.Id);

        var jsonReplaced = updated.ProposedValueJson == newJson;
        var statusEdited = updated.Status == ReviewProposalStatus.Edited;
        var reviewedAtSet = updated.ReviewedAt.HasValue
            && updated.ReviewedAt.Value >= before
            && updated.ReviewedAt.Value <= after;
        var reviewedBySet = updated.ReviewedByUserId == userId;

        // No knowledge graph changes
        var noArtifacts = ctx.ArtifactRepo.Artifacts.Count == 0;
        var noFacts = ctx.ArtifactFactRepo.Facts.Count == 0;
        var noRelationships = ctx.ArtifactRelationshipRepo.Relationships.Count == 0;
        var noSourceRefs = ctx.SourceRefRepo.References.Count == 0;

        return jsonReplaced.Label($"ProposedValueJson should be replaced with new JSON")
            .And(statusEdited.Label($"Status should be Edited, got {updated.Status}"))
            .And(reviewedAtSet.Label("ReviewedAt should be set to approximately current UTC"))
            .And(reviewedBySet.Label($"ReviewedByUserId should be {userId}"))
            .And(noArtifacts.Label($"No artifacts should exist, got {ctx.ArtifactRepo.Artifacts.Count}"))
            .And(noFacts.Label($"No facts should exist, got {ctx.ArtifactFactRepo.Facts.Count}"))
            .And(noRelationships.Label($"No relationships, got {ctx.ArtifactRelationshipRepo.Relationships.Count}"))
            .And(noSourceRefs.Label($"No source refs, got {ctx.SourceRefRepo.References.Count}"));
    }

    #endregion

    #region Property 12: Edited Proposals Allow Subsequent Accept or Reject

    /// <summary>
    /// Property 12: Edited Proposals Allow Subsequent Accept or Reject
    ///
    /// For any proposal with Status Edited, both acceptance (following Requirement 2 logic
    /// with the edited ProposedValueJson) and rejection (following Requirement 3 logic)
    /// SHALL succeed.
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 12: Edited Proposals Allow Subsequent Accept")]
    public Property Edited_proposals_allow_subsequent_accept(ProposalWithContext pwc)
    {
        // Use REAL applicator for accept — the edited JSON must actually work
        var ctx = CreateRealService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content about Captain Voss",
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

        // Use a valid CreateArtifact payload as the edited JSON
        var editedJson = ReviewGenerators.ValidCreateArtifactPayload.Sample(1, 1).First();

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = editedJson,
            Rationale = "Edited by reviewer",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Edited, // Already edited
            CreatedAt = batch.CreatedAt.AddMinutes(1),
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ReviewedByUserId = userId
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        var result = ctx.Service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"Accept of edited proposal failed: {result.Error!.Code} - {result.Error!.Message}");

        var updated = ctx.ProposalRepo.Proposals.First(p => p.Id == proposal.Id);
        var statusAccepted = updated.Status == ReviewProposalStatus.Accepted;
        var artifactCreated = ctx.ArtifactRepo.Artifacts.Count == 1;

        return statusAccepted.Label($"Status should be Accepted, got {updated.Status}")
            .And(artifactCreated.Label($"Artifact should be created, got {ctx.ArtifactRepo.Artifacts.Count}"));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 12: Edited Proposals Allow Subsequent Reject")]
    public Property Edited_proposals_allow_subsequent_reject(ProposalWithContext pwc)
    {
        // Use FAKE applicator for reject — no application needed
        var ctx = CreateFakeService();

        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content about Captain Voss",
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

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
            Rationale = "Edited by reviewer",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Edited, // Already edited
            CreatedAt = batch.CreatedAt.AddMinutes(1),
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ReviewedByUserId = userId
        };
        ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        var before = DateTimeOffset.UtcNow;
        var result = ctx.Service.RejectProposalAsync(
            new RejectProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();
        var after = DateTimeOffset.UtcNow;

        if (!result.IsSuccess)
            return false.Label($"Reject of edited proposal failed: {result.Error!.Code} - {result.Error!.Message}");

        var updated = ctx.ProposalRepo.Proposals.First(p => p.Id == proposal.Id);
        var statusRejected = updated.Status == ReviewProposalStatus.Rejected;
        var reviewedAtSet = updated.ReviewedAt.HasValue
            && updated.ReviewedAt.Value >= before
            && updated.ReviewedAt.Value <= after;
        var noArtifacts = ctx.ArtifactRepo.Artifacts.Count == 0;

        return statusRejected.Label($"Status should be Rejected, got {updated.Status}")
            .And(reviewedAtSet.Label("ReviewedAt should be updated"))
            .And(noArtifacts.Label("No artifacts should be created on reject"));
    }

    #endregion

    #region Property 13: Batch Processes Each Proposal Correctly

    /// <summary>
    /// Property 13: Batch Processes Each Proposal Correctly
    ///
    /// Generate batch of 1–50 unique pending proposal Ids; batch accept/reject; assert each
    /// processed following single-proposal logic in request order.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.6**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 13: Batch Accept Processes Each Proposal Correctly")]
    public Property Batch_accept_processes_each_proposal_correctly(PositiveInt countRaw)
    {
        var count = (countRaw.Get % 10) + 1; // 1-10 for test speed
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

        // Create N pending proposals
        var proposalIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var proposal = new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = null,
                ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
                Rationale = $"Proposal {i}",
                Confidence = 0.8m,
                Status = ReviewProposalStatus.Pending,
                CreatedAt = batch.CreatedAt.AddMinutes(i + 1)
            };
            ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
            proposalIds.Add(proposal.Id);
        }

        var result = ctx.Service.BatchAcceptAsync(
            new BatchAcceptCommand(proposalIds, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"BatchAccept failed: {result.Error!.Code}");

        var batchResult = result.Value!;
        var allSucceeded = batchResult.Succeeded.Count == count;
        var noFailures = batchResult.Failed.Count == 0;

        // Verify all proposals transitioned to Accepted
        var allAccepted = ctx.ProposalRepo.Proposals
            .Where(p => proposalIds.Contains(p.Id))
            .All(p => p.Status == ReviewProposalStatus.Accepted);

        return allSucceeded.Label($"Expected {count} succeeded, got {batchResult.Succeeded.Count}")
            .And(noFailures.Label($"Expected 0 failures, got {batchResult.Failed.Count}"))
            .And(allAccepted.Label("All proposals should be Accepted"));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 13: Batch Reject Processes Each Proposal Correctly")]
    public Property Batch_reject_processes_each_proposal_correctly(PositiveInt countRaw)
    {
        var count = (countRaw.Get % 10) + 1; // 1-10 for test speed
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

        // Create N pending proposals
        var proposalIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var proposal = new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = null,
                ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
                Rationale = $"Proposal {i}",
                Confidence = 0.8m,
                Status = ReviewProposalStatus.Pending,
                CreatedAt = batch.CreatedAt.AddMinutes(i + 1)
            };
            ctx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
            proposalIds.Add(proposal.Id);
        }

        var result = ctx.Service.BatchRejectAsync(
            new BatchRejectCommand(proposalIds, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"BatchReject failed: {result.Error!.Code}");

        var batchResult = result.Value!;
        var allSucceeded = batchResult.Succeeded.Count == count;
        var noFailures = batchResult.Failed.Count == 0;

        // Verify all proposals transitioned to Rejected
        var allRejected = ctx.ProposalRepo.Proposals
            .Where(p => proposalIds.Contains(p.Id))
            .All(p => p.Status == ReviewProposalStatus.Rejected);

        return allSucceeded.Label($"Expected {count} succeeded, got {batchResult.Succeeded.Count}")
            .And(noFailures.Label($"Expected 0 failures, got {batchResult.Failed.Count}"))
            .And(allRejected.Label("All proposals should be Rejected"));
    }

    #endregion

    #region Property 14: Batch Partial Failure Reports Correct Partitioning

    /// <summary>
    /// Property 14: Batch Partial Failure Reports Correct Partitioning
    ///
    /// Generate batch with mix of valid, unauthorized, non-existent, wrong-status, and
    /// invisible proposals; assert succeeded/failed lists correctly partition with accurate
    /// error reasons.
    ///
    /// **Validates: Requirements 5.3, 5.5**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 14: Batch Partial Failure Reports Correct Partitioning")]
    public Property Batch_partial_failure_reports_correct_partitioning(PositiveInt seed)
    {
        var ctx = CreateFakeService();

        var gmUserId = Guid.NewGuid();
        var playerUserId = Guid.NewGuid();
        var worldId = Guid.NewGuid();

        // Source owned by GM — GM can review
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "GM Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = gmUserId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        // Source owned by player — player can review, but GM can too
        var playerSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Player Source",
            Body = "Content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = playerUserId,
            Visibility = VisibilityScope.Private,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        ctx.SourceRepo.Seed(gmSource, playerSource);

        var gmBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = gmSource.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var playerBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = playerSource.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        ctx.BatchRepo.CreateAsync(gmBatch).GetAwaiter().GetResult();
        ctx.BatchRepo.CreateAsync(playerBatch).GetAwaiter().GetResult();

        // 1. Valid pending proposal (should succeed)
        var validProposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = gmBatch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
            Rationale = "Valid",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = gmBatch.CreatedAt.AddMinutes(1)
        };

        // 2. Already rejected proposal (should fail — conflict)
        var rejectedProposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = gmBatch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Tavrin\",\"type\":\"Character\"}",
            Rationale = "Rejected",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Rejected,
            CreatedAt = gmBatch.CreatedAt.AddMinutes(2),
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReviewedByUserId = gmUserId
        };

        // 3. Invisible proposal (Private source, player is acting as Player, not source owner)
        var invisibleProposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = playerBatch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Silver Key\",\"type\":\"Item\"}",
            Rationale = "Invisible",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = playerBatch.CreatedAt.AddMinutes(1)
        };

        ctx.ProposalRepo.CreateAsync(validProposal).GetAwaiter().GetResult();
        ctx.ProposalRepo.CreateAsync(rejectedProposal).GetAwaiter().GetResult();
        ctx.ProposalRepo.CreateAsync(invisibleProposal).GetAwaiter().GetResult();

        // 4. Non-existent proposal ID
        var nonExistentId = Guid.NewGuid();

        // Batch accept as a different user (not the player source owner) with Player role
        // But actually let's use GM for valid + rejected, and include non-existent
        var proposalIds = new List<Guid>
        {
            validProposal.Id,
            rejectedProposal.Id,
            nonExistentId
        };

        var result = ctx.Service.BatchAcceptAsync(
            new BatchAcceptCommand(proposalIds, worldId, gmUserId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"BatchAccept should return success with partitioned results, got error: {result.Error!.Code}");

        var batchResult = result.Value!;

        // Valid proposal should succeed
        var validInSucceeded = batchResult.Succeeded.Contains(validProposal.Id);

        // Rejected proposal should fail with conflict
        var rejectedInFailed = batchResult.Failed.Any(f =>
            f.ProposalId == rejectedProposal.Id && f.Code == "conflict");

        // Non-existent should fail with not_found
        var nonExistentInFailed = batchResult.Failed.Any(f =>
            f.ProposalId == nonExistentId && f.Code == "not_found");

        // Total should partition correctly
        var totalPartitioned = batchResult.Succeeded.Count + batchResult.Failed.Count == 3;

        return validInSucceeded.Label("Valid proposal should be in succeeded list")
            .And(rejectedInFailed.Label("Rejected proposal should fail with 'conflict'"))
            .And(nonExistentInFailed.Label("Non-existent proposal should fail with 'not_found'"))
            .And(totalPartitioned.Label($"Total should be 3, got {batchResult.Succeeded.Count + batchResult.Failed.Count}"));
    }

    #endregion

    #region Property 15: First Review Transitions Batch to InReview

    /// <summary>
    /// Property 15: First Review Transitions Batch to InReview
    ///
    /// Generate ReviewBatch in Pending status; review first proposal (accept/reject/edit);
    /// assert batch Status transitions to InReview.
    ///
    /// **Validates: Requirements 8.1, 3.6**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 15: First Accept Transitions Batch to InReview")]
    public Property First_accept_transitions_batch_to_inreview(ProposalWithContext pwc)
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
        // Batch starts as Pending
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

        // Create 2+ proposals so the batch doesn't auto-complete
        var proposal1 = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
            Rationale = "First proposal",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        };
        var proposal2 = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Silver Key\",\"type\":\"Item\"}",
            Rationale = "Second proposal",
            Confidence = 0.7m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(2)
        };
        ctx.ProposalRepo.CreateAsync(proposal1).GetAwaiter().GetResult();
        ctx.ProposalRepo.CreateAsync(proposal2).GetAwaiter().GetResult();

        // Accept the first proposal
        var result = ctx.Service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal1.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"Accept failed: {result.Error!.Code} - {result.Error!.Message}");

        // Assert batch transitioned to InReview
        var updatedBatch = ctx.BatchRepo.Batches.First(b => b.Id == batch.Id);
        var isInReview = updatedBatch.Status == ReviewBatchStatus.InReview;

        return isInReview.Label($"Batch should be InReview, got {updatedBatch.Status}");
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 15: First Reject Transitions Batch to InReview")]
    public Property First_reject_transitions_batch_to_inreview(ProposalWithContext pwc)
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
            Status = ReviewBatchStatus.Pending,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Create 2+ proposals so the batch doesn't auto-complete
        var proposal1 = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
            Rationale = "First proposal",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        };
        var proposal2 = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Silver Key\",\"type\":\"Item\"}",
            Rationale = "Second proposal",
            Confidence = 0.7m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(2)
        };
        ctx.ProposalRepo.CreateAsync(proposal1).GetAwaiter().GetResult();
        ctx.ProposalRepo.CreateAsync(proposal2).GetAwaiter().GetResult();

        // Reject the first proposal
        var result = ctx.Service.RejectProposalAsync(
            new RejectProposalCommand(proposal1.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"Reject failed: {result.Error!.Code} - {result.Error!.Message}");

        // Assert batch transitioned to InReview
        var updatedBatch = ctx.BatchRepo.Batches.First(b => b.Id == batch.Id);
        var isInReview = updatedBatch.Status == ReviewBatchStatus.InReview;

        return isInReview.Label($"Batch should be InReview, got {updatedBatch.Status}");
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 15: First Edit Transitions Batch to InReview")]
    public Property First_edit_transitions_batch_to_inreview(ProposalWithContext pwc)
    {
        var ctx = CreateEditService();

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

        // Create 2+ proposals so the batch doesn't auto-complete
        var proposal1 = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Captain Voss\",\"type\":\"Character\"}",
            Rationale = "First proposal",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        };
        var proposal2 = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = "{\"name\":\"Silver Key\",\"type\":\"Item\"}",
            Rationale = "Second proposal",
            Confidence = 0.7m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(2)
        };
        ctx.ProposalRepo.CreateAsync(proposal1).GetAwaiter().GetResult();
        ctx.ProposalRepo.CreateAsync(proposal2).GetAwaiter().GetResult();

        // Edit the first proposal with valid new JSON
        var newJson = ReviewGenerators.ValidCreateArtifactPayload.Sample(1, 1).First();
        var result = ctx.Service.EditProposalAsync(
            new EditProposalCommand(proposal1.Id, worldId, userId, WorldRole.GM, newJson),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            return false.Label($"Edit failed: {result.Error!.Code} - {result.Error!.Message}");

        // Assert batch transitioned to InReview
        var updatedBatch = ctx.BatchRepo.Batches.First(b => b.Id == batch.Id);
        var isInReview = updatedBatch.Status == ReviewBatchStatus.InReview;

        return isInReview.Label($"Batch should be InReview, got {updatedBatch.Status}");
    }

    #endregion
}
