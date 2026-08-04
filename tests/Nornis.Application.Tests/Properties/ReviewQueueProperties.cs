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
/// The order the review queue hands proposals back in, and where it draws the page boundary.
/// </summary>
[TestFixture]
[Category("Feature: review-proposal-workflow")]
public class ReviewQueueProperties
{
    #region Property 21: Review Queue Ordering

    /// <summary>
    /// Property 21: Review Queue Ordering
    ///
    /// Generate proposals with random timestamps across multiple batches; list queue;
    /// assert proposals ordered by CreatedAt ascending within each batch,
    /// batches ordered by CreatedAt ascending.
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 21: Review Queue Ordering")]
    public Property Review_queue_orders_by_batch_then_proposal_created_at(PositiveInt batchCountRaw, PositiveInt proposalsPerBatchRaw)
    {
        var batchCount = (batchCountRaw.Get % 3) + 2; // 2-4 batches
        var proposalsPerBatch = (proposalsPerBatchRaw.Get % 4) + 2; // 2-5 proposals per batch
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
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 5)]
    [Description("Feature: review-proposal-workflow, Property 22: Review Queue Pagination Over 200")]
    public Property Review_queue_pagination_over_200_returns_hasmore_true(PositiveInt extraRaw)
    {
        var extraCount = (extraRaw.Get % 10) + 1; // 1-10 extra beyond 200
        var totalCount = 200 + extraCount;
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
