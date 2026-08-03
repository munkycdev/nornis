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
/// Who can see a proposal, who may act on it, and what visibility the entity it creates inherits.
/// The not-found-rather-than-forbidden rule lives here too: it is a visibility decision, not an
/// error-handling one.
/// </summary>
[TestFixture]
[Category("Feature: review-proposal-workflow")]
public class ProposalAccessProperties
{
    #region Property 1: Visibility Filtering

    /// <summary>
    /// Property 1: Visibility Filtering
    ///
    /// For any world with sources of mixed VisibilityScope owned by different users,
    /// and any world member requesting the review queue:
    /// - GM sees all pending proposals regardless of source author or visibility
    /// - Player sees only pending proposals from sources the Player created
    /// - Observer sees zero proposals
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 7.1, 7.2, 7.3**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 1: Visibility Filtering")]
    public Property GM_sees_all_Player_sees_own_Observer_sees_none(ReviewScenario scenario)
    {
        var service = SeededWithFakes(scenario).Service;

        // GM query
        var gmQuery = new ReviewQueueQuery(scenario.WorldId, scenario.GmUserId, WorldRole.GM);
        var gmResult = service.ListReviewQueueAsync(gmQuery, CancellationToken.None).GetAwaiter().GetResult();

        // Player query
        var playerQuery = new ReviewQueueQuery(scenario.WorldId, scenario.PlayerUserId, WorldRole.Player);
        var playerResult = service.ListReviewQueueAsync(playerQuery, CancellationToken.None).GetAwaiter().GetResult();

        // Observer query
        var observerQuery = new ReviewQueueQuery(scenario.WorldId, scenario.ObserverUserId, WorldRole.Observer);
        var observerResult = service.ListReviewQueueAsync(observerQuery, CancellationToken.None).GetAwaiter().GetResult();

        // Compute expected sets
        var allPendingProposals = scenario.Proposals
            .Where(p => p.Status == ReviewProposalStatus.Pending)
            .ToList();

        var playerOwnedSourceIds = scenario.Sources
            .Where(s => s.CreatedByUserId == scenario.PlayerUserId)
            .Select(s => s.Id)
            .ToHashSet();

        var playerVisibleBatchIds = scenario.Batches
            .Where(b => playerOwnedSourceIds.Contains(b.SourceId))
            .Select(b => b.Id)
            .ToHashSet();

        var expectedPlayerProposals = allPendingProposals
            .Where(p => playerVisibleBatchIds.Contains(p.ReviewBatchId))
            .ToList();

        var gmSeesAll = gmResult.IsSuccess
            && gmResult.Value!.Proposals.Count == allPendingProposals.Count;

        var playerSeesOnlyOwn = playerResult.IsSuccess
            && playerResult.Value!.Proposals.Count == expectedPlayerProposals.Count
            && playerResult.Value.Proposals.All(p => expectedPlayerProposals.Any(ep => ep.Id == p.Id));

        var observerSeesNone = observerResult.IsSuccess
            && observerResult.Value!.Proposals.Count == 0;

        return gmSeesAll
            .Label($"GM should see all {allPendingProposals.Count} pending proposals, got {gmResult.Value?.Proposals.Count ?? -1}")
            .And(playerSeesOnlyOwn
                .Label($"Player should see {expectedPlayerProposals.Count} own-source proposals, got {playerResult.Value?.Proposals.Count ?? -1}"))
            .And(observerSeesNone
                .Label($"Observer should see 0 proposals, got {observerResult.Value?.Proposals.Count ?? -1}"));
    }

    #endregion

    #region Property 2: Authorization Enforcement

    /// <summary>
    /// Property 2: Authorization Enforcement
    ///
    /// For any review operation (accept, reject, or edit) and any proposal in a world:
    /// - GM is always authorized regardless of source author
    /// - Player is authorized only if the source was created by that Player
    /// - Observer is always denied with 403
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 2: Authorization Enforcement")]
    public Property Authorization_enforced_per_role_and_source_ownership(ReviewScenario scenario)
    {
        if (scenario.Proposals.Count == 0)
            return true.ToProperty();

        var service = SeededWithFakes(scenario).Service;

        // Pick first pending proposal
        var proposal = scenario.Proposals.First(p => p.Status == ReviewProposalStatus.Pending);
        var batch = scenario.Batches.First(b => b.Id == proposal.ReviewBatchId);
        var source = scenario.Sources.First(s => s.Id == batch.SourceId);

        // GM accept — should succeed (not 403)
        var gmAccept = service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, scenario.WorldId, scenario.GmUserId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        var gmAuthorized = gmAccept.IsSuccess || gmAccept.Error!.StatusCode != 403;

        // Player accept — authorized only if source is owned by player
        var playerAccept = service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, scenario.WorldId, scenario.PlayerUserId, WorldRole.Player),
            CancellationToken.None).GetAwaiter().GetResult();

        bool playerCorrect;
        if (source.CreatedByUserId == scenario.PlayerUserId)
        {
            // Player owns source → should not get 403
            playerCorrect = playerAccept.IsSuccess || playerAccept.Error!.StatusCode != 403;
        }
        else
        {
            // Player doesn't own source → should get 403 or 404 (invisible = not found)
            playerCorrect = !playerAccept.IsSuccess
                && (playerAccept.Error!.StatusCode == 403 || playerAccept.Error!.StatusCode == 404);
        }

        // Observer accept — always denied
        var observerAccept = service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, scenario.WorldId, scenario.ObserverUserId, WorldRole.Observer),
            CancellationToken.None).GetAwaiter().GetResult();

        // Observer gets 404 (invisible) because Observer can never see any proposals
        var observerDenied = !observerAccept.IsSuccess
            && (observerAccept.Error!.StatusCode == 403 || observerAccept.Error!.StatusCode == 404);

        return gmAuthorized
            .Label("GM should be authorized for any proposal")
            .And(playerCorrect
                .Label($"Player authorization: source owned by player={source.CreatedByUserId == scenario.PlayerUserId}"))
            .And(observerDenied
                .Label("Observer should always be denied"));
    }

    #endregion

    #region Property 3: Invisible Proposals Treated as Not-Found

    /// <summary>
    /// Property 3: Invisible Proposals Treated as Not-Found
    ///
    /// For any proposal that a user cannot see due to visibility rules,
    /// any review operation SHALL respond with a not-found error (404)
    /// rather than a forbidden error (403).
    ///
    /// **Validates: Requirements 3.5, 7.4, 7.6**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 3: Invisible Proposals Treated as Not-Found")]
    public Property Invisible_proposals_return_not_found_not_forbidden(ReviewScenario scenario)
    {
        // Find proposals from sources NOT owned by the player
        var playerOwnedSourceIds = scenario.Sources
            .Where(s => s.CreatedByUserId == scenario.PlayerUserId)
            .Select(s => s.Id)
            .ToHashSet();

        var invisibleBatchIds = scenario.Batches
            .Where(b => !playerOwnedSourceIds.Contains(b.SourceId))
            .Select(b => b.Id)
            .ToHashSet();

        var invisibleProposals = scenario.Proposals
            .Where(p => invisibleBatchIds.Contains(p.ReviewBatchId) && p.Status == ReviewProposalStatus.Pending)
            .ToList();

        if (invisibleProposals.Count == 0)
            return true.ToProperty(); // No invisible proposals in this scenario

        var service = SeededWithFakes(scenario).Service;

        var allReturnNotFound = true;
        var failureLabels = new List<string>();

        foreach (var proposal in invisibleProposals.Take(3)) // Test up to 3 to keep fast
        {
            // Attempt accept as Player
            var acceptResult = service.AcceptProposalAsync(
                new AcceptProposalCommand(proposal.Id, scenario.WorldId, scenario.PlayerUserId, WorldRole.Player),
                CancellationToken.None).GetAwaiter().GetResult();

            if (acceptResult.IsSuccess || acceptResult.Error!.StatusCode != 404)
            {
                allReturnNotFound = false;
                failureLabels.Add($"Accept proposal {proposal.Id}: expected 404, got {(acceptResult.IsSuccess ? "success" : acceptResult.Error!.StatusCode.ToString())}");
            }

            // Attempt reject as Player
            var rejectResult = service.RejectProposalAsync(
                new RejectProposalCommand(proposal.Id, scenario.WorldId, scenario.PlayerUserId, WorldRole.Player),
                CancellationToken.None).GetAwaiter().GetResult();

            if (rejectResult.IsSuccess || rejectResult.Error!.StatusCode != 404)
            {
                allReturnNotFound = false;
                failureLabels.Add($"Reject proposal {proposal.Id}: expected 404, got {(rejectResult.IsSuccess ? "success" : rejectResult.Error!.StatusCode.ToString())}");
            }

            // Attempt edit as Player
            var editResult = service.EditProposalAsync(
                new EditProposalCommand(proposal.Id, scenario.WorldId, scenario.PlayerUserId, WorldRole.Player,
                    "{\"name\":\"Test\",\"type\":\"Character\"}"),
                CancellationToken.None).GetAwaiter().GetResult();

            if (editResult.IsSuccess || editResult.Error!.StatusCode != 404)
            {
                allReturnNotFound = false;
                failureLabels.Add($"Edit proposal {proposal.Id}: expected 404, got {(editResult.IsSuccess ? "success" : editResult.Error!.StatusCode.ToString())}");
            }
        }

        return allReturnNotFound
            .Label(failureLabels.Count > 0
                ? string.Join("; ", failureLabels)
                : "All invisible proposals correctly returned 404 not-found");
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
        var ctx = ReviewHarness.WithRealApplicator();

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
        var ctx = ReviewHarness.WithRealApplicator();

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
}
