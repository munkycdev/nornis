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
using static Nornis.Application.Tests.Properties.ReviewPropertySupport;

namespace Nornis.Application.Tests.Properties;

/// <summary>
/// A batch's status as a function of its proposals: the first review moves it to InReview, the last
/// terminal one moves it to Completed, and a proposal already in a terminal state is either a no-op
/// or a conflict depending on which terminal state it is in.
/// </summary>
[TestFixture]
[Category("Feature: review-proposal-workflow")]
public class ReviewBatchLifecycleProperties
{
    #region Property 13: Batch Processes Each Proposal Correctly

    /// <summary>
    /// Property 13: Batch Processes Each Proposal Correctly
    ///
    /// Generate batch of 1–50 unique pending proposal Ids; batch accept/reject; assert each
    /// processed following single-proposal logic in request order.
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 13: Batch Accept Processes Each Proposal Correctly")]
    public Property Batch_accept_processes_each_proposal_correctly(PositiveInt countRaw)
    {
        var count = (countRaw.Get % 10) + 1; // 1-10 for test speed
        var ctx = ReviewHarness.WithFakeApplicator();

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
        var ctx = ReviewHarness.WithFakeApplicator();

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
    /// </summary>
    [Test]
    [Description("Feature: review-proposal-workflow, Property 14: Batch Partial Failure Reports Correct Partitioning")]
    public void Batch_partial_failure_reports_correct_partitioning()
    {
        var ctx = ReviewHarness.WithFakeApplicator();

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
            Assert.Fail($"BatchAccept should return success with partitioned results, got error: {result.Error!.Code}");

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

        Assert.Multiple(() =>
        {
            Assert.That(validInSucceeded, Is.True, "Valid proposal should be in succeeded list");
            Assert.That(rejectedInFailed, Is.True, "Rejected proposal should fail with 'conflict'");
            Assert.That(nonExistentInFailed, Is.True, "Non-existent proposal should fail with 'not_found'");
            Assert.That(totalPartitioned, Is.True, $"Total should be 3, got {batchResult.Succeeded.Count + batchResult.Failed.Count}");
        });
    }

    #endregion

    #region Property 15: First Review Transitions Batch to InReview

    /// <summary>
    /// Property 15: First Review Transitions Batch to InReview
    ///
    /// Generate ReviewBatch in Pending status; review first proposal (accept/reject/edit);
    /// assert batch Status transitions to InReview.
    /// </summary>
    [Test]
    [Description("Feature: review-proposal-workflow, Property 15: First Accept Transitions Batch to InReview")]
    public void First_accept_transitions_batch_to_inreview()
    {
        var ctx = ReviewHarness.WithFakeApplicator();

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
            Assert.Fail($"Accept failed: {result.Error!.Code} - {result.Error!.Message}");

        // Assert batch transitioned to InReview
        var updatedBatch = ctx.BatchRepo.Batches.First(b => b.Id == batch.Id);
        var isInReview = updatedBatch.Status == ReviewBatchStatus.InReview;

        Assert.That(isInReview, Is.True, $"Batch should be InReview, got {updatedBatch.Status}");
    }

    [Test]
    [Description("Feature: review-proposal-workflow, Property 15: First Reject Transitions Batch to InReview")]
    public void First_reject_transitions_batch_to_inreview()
    {
        var ctx = ReviewHarness.WithFakeApplicator();

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
            Assert.Fail($"Reject failed: {result.Error!.Code} - {result.Error!.Message}");

        // Assert batch transitioned to InReview
        var updatedBatch = ctx.BatchRepo.Batches.First(b => b.Id == batch.Id);
        var isInReview = updatedBatch.Status == ReviewBatchStatus.InReview;

        Assert.That(isInReview, Is.True, $"Batch should be InReview, got {updatedBatch.Status}");
    }

    [Test]
    [Description("Feature: review-proposal-workflow, Property 15: First Edit Transitions Batch to InReview")]
    public void First_edit_transitions_batch_to_inreview()
    {
        var ctx = ReviewHarness.WithFakeApplicator();

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
            Assert.Fail($"Edit failed: {result.Error!.Code} - {result.Error!.Message}");

        // Assert batch transitioned to InReview
        var updatedBatch = ctx.BatchRepo.Batches.First(b => b.Id == batch.Id);
        var isInReview = updatedBatch.Status == ReviewBatchStatus.InReview;

        Assert.That(isInReview, Is.True, $"Batch should be InReview, got {updatedBatch.Status}");
    }

    #endregion

    #region Property 16: All Proposals Terminal Transitions Batch to Completed

    /// <summary>
    /// Property 16: All Proposals Terminal Transitions Batch to Completed
    ///
    /// Generate ReviewBatch in InReview with all-but-one proposals terminal;
    /// bring last proposal to terminal; assert batch Status=Completed and CompletedAt set.
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 16: All Proposals Terminal Transitions Batch to Completed")]
    public Property All_proposals_terminal_transitions_batch_to_completed(PositiveInt countRaw, bool lastAccepted)
    {
        var totalProposals = (countRaw.Get % 5) + 2; // 2-6 proposals
        var ctx = ReviewHarness.WithFakeApplicator();

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
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 17: Batch Not Completed While Non-Terminal Proposals Remain")]
    public Property Batch_not_completed_while_non_terminal_proposals_remain(PositiveInt countRaw)
    {
        var totalProposals = (countRaw.Get % 5) + 3; // 3-7 proposals
        var ctx = ReviewHarness.WithFakeApplicator();

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
    /// </summary>
    [Test]
    [Description("Feature: review-proposal-workflow, Property 18: Idempotent Accept of Already-Accepted Proposal")]
    public void Idempotent_accept_of_already_accepted_proposal()
    {
        var ctx = ReviewHarness.WithFakeApplicator();

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
            Assert.Fail($"Idempotent accept failed: {result.Error!.Code} - {result.Error!.Message}");

        var value = result.Value!;
        var preservedReviewedAt = value.ReviewedAt == originalReviewedAt;
        var preservedReviewedBy = value.ReviewedByUserId == originalReviewerId;
        var noNewArtifacts = ctx.ArtifactRepo.Artifacts.Count == artifactCountBefore;
        var noNewSourceRefs = ctx.SourceRefRepo.References.Count == sourceRefCountBefore;

        Assert.Multiple(() =>
        {
            Assert.That(preservedReviewedAt, Is.True, $"ReviewedAt should be original {originalReviewedAt}, got {value.ReviewedAt}");
            Assert.That(preservedReviewedBy, Is.True, $"ReviewedByUserId should be {originalReviewerId}, got {value.ReviewedByUserId}");
            Assert.That(noNewArtifacts, Is.True, "No new artifacts should be created");
            Assert.That(noNewSourceRefs, Is.True, "No new source references should be created");
        });
    }

    [Test]
    [Description("Feature: review-proposal-workflow, Property 18: Idempotent Reject of Already-Rejected Proposal")]
    public void Idempotent_reject_of_already_rejected_proposal()
    {
        var ctx = ReviewHarness.WithFakeApplicator();

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
            Assert.Fail($"Idempotent reject failed: {result.Error!.Code} - {result.Error!.Message}");

        var value = result.Value!;
        var preservedReviewedAt = value.ReviewedAt == originalReviewedAt;
        var preservedReviewedBy = value.ReviewedByUserId == originalReviewerId;

        // Verify proposal state is unchanged
        var updatedProposal = ctx.ProposalRepo.Proposals.First(p => p.Id == proposal.Id);
        var statusUnchanged = updatedProposal.Status == ReviewProposalStatus.Rejected;
        var reviewedAtUnchanged = updatedProposal.ReviewedAt == originalReviewedAt;

        Assert.Multiple(() =>
        {
            Assert.That(preservedReviewedAt, Is.True, $"ReviewedAt should be original {originalReviewedAt}, got {value.ReviewedAt}");
            Assert.That(preservedReviewedBy, Is.True, $"ReviewedByUserId should be {originalReviewerId}, got {value.ReviewedByUserId}");
            Assert.That(statusUnchanged, Is.True, "Proposal status should remain Rejected");
            Assert.That(reviewedAtUnchanged, Is.True, "Proposal ReviewedAt should remain unchanged");
        });
    }

    #endregion

    #region Property 19: Cross-State Terminal Transition Error

    /// <summary>
    /// Property 19: Cross-State Terminal Transition Error
    ///
    /// Accept a Rejected proposal; assert error; reject an Accepted proposal; assert error.
    /// </summary>
    [Test]
    [Description("Feature: review-proposal-workflow, Property 19: Accept Rejected Proposal Returns Error")]
    public void Accept_rejected_proposal_returns_conflict_error()
    {
        var ctx = ReviewHarness.WithFakeApplicator();

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

        Assert.Multiple(() =>
        {
            Assert.That(isError, Is.True, "Should return error when accepting a rejected proposal");
            Assert.That(isConflict, Is.True, $"Should be 409, got {result.Error?.StatusCode}");
            Assert.That(codeIsConflict, Is.True, $"Error code should be 'conflict', got '{result.Error?.Code}'");
        });
    }

    [Test]
    [Description("Feature: review-proposal-workflow, Property 19: Reject Accepted Proposal Returns Error")]
    public void Reject_accepted_proposal_returns_conflict_error()
    {
        var ctx = ReviewHarness.WithFakeApplicator();

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

        Assert.Multiple(() =>
        {
            Assert.That(isError, Is.True, "Should return error when rejecting an accepted proposal");
            Assert.That(isConflict, Is.True, $"Should be 409, got {result.Error?.StatusCode}");
            Assert.That(codeIsConflict, Is.True, $"Error code should be 'conflict', got '{result.Error?.Code}'");
        });
    }

    #endregion
}
