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
/// Property-based tests for ReviewService covering visibility, authorization,
/// not-found semantics, accept transitions, and CreateArtifact acceptance.
/// Uses FsCheck.NUnit with custom Arbitraries and in-memory fakes.
/// </summary>
[TestFixture]
[Category("Feature: review-proposal-workflow")]
public class ReviewServicePropertyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region Helpers

    /// <summary>
    /// Creates a ReviewService wired to in-memory fakes with the given scenario data seeded.
    /// Uses FakeProposalValidator and FakeProposalApplicator for properties 1-4.
    /// </summary>
    private static (ReviewService Service, InMemoryReviewProposalRepository ProposalRepo,
        InMemoryArtifactRepository ArtifactRepo, InMemorySourceReferenceRepository SourceRefRepo)
        CreateServiceWithFakes(ReviewScenario scenario)
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

        // Seed data
        sourceRepo.Seed(scenario.Sources);
        foreach (var batch in scenario.Batches)
            batchRepo.CreateAsync(batch).GetAwaiter().GetResult();
        foreach (var proposal in scenario.Proposals)
            proposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

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

        return (service, proposalRepo, artifactRepo, sourceRefRepo);
    }

    /// <summary>
    /// Creates a ReviewService with REAL ProposalValidator and ProposalApplicator
    /// for full integration property tests (Properties 4 and 5).
    /// </summary>
    private static (ReviewService Service, InMemoryReviewProposalRepository ProposalRepo,
        InMemoryArtifactRepository ArtifactRepo, InMemorySourceReferenceRepository SourceRefRepo,
        InMemoryReviewBatchRepository BatchRepo, InMemorySourceRepository SourceRepo)
        CreateServiceWithRealApplicator(ProposalWithContext ctx)
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

        // Seed data
        sourceRepo.Seed(ctx.Source);
        batchRepo.CreateAsync(ctx.Batch).GetAwaiter().GetResult();
        proposalRepo.CreateAsync(ctx.Proposal).GetAwaiter().GetResult();

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

        return (service, proposalRepo, artifactRepo, sourceRefRepo, batchRepo, sourceRepo);
    }

    #endregion

    #region Property 1: Visibility Filtering

    /// <summary>
    /// Property 1: Visibility Filtering
    ///
    /// For any campaign with sources of mixed VisibilityScope owned by different users,
    /// and any campaign member requesting the review queue:
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
        var (service, _, _, _) = CreateServiceWithFakes(scenario);

        // GM query
        var gmQuery = new ReviewQueueQuery(scenario.CampaignId, scenario.GmUserId, CampaignRole.GM);
        var gmResult = service.ListReviewQueueAsync(gmQuery, CancellationToken.None).GetAwaiter().GetResult();

        // Player query
        var playerQuery = new ReviewQueueQuery(scenario.CampaignId, scenario.PlayerUserId, CampaignRole.Player);
        var playerResult = service.ListReviewQueueAsync(playerQuery, CancellationToken.None).GetAwaiter().GetResult();

        // Observer query
        var observerQuery = new ReviewQueueQuery(scenario.CampaignId, scenario.ObserverUserId, CampaignRole.Observer);
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
    /// For any review operation (accept, reject, or edit) and any proposal in a campaign:
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

        var (service, _, _, _) = CreateServiceWithFakes(scenario);

        // Pick first pending proposal
        var proposal = scenario.Proposals.First(p => p.Status == ReviewProposalStatus.Pending);
        var batch = scenario.Batches.First(b => b.Id == proposal.ReviewBatchId);
        var source = scenario.Sources.First(s => s.Id == batch.SourceId);

        // GM accept — should succeed (not 403)
        var gmAccept = service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, scenario.CampaignId, scenario.GmUserId, CampaignRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();

        var gmAuthorized = gmAccept.IsSuccess || gmAccept.Error!.StatusCode != 403;

        // Player accept — authorized only if source is owned by player
        var playerAccept = service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, scenario.CampaignId, scenario.PlayerUserId, CampaignRole.Player),
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
            new AcceptProposalCommand(proposal.Id, scenario.CampaignId, scenario.ObserverUserId, CampaignRole.Observer),
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

        var (service, _, _, _) = CreateServiceWithFakes(scenario);

        var allReturnNotFound = true;
        var failureLabels = new List<string>();

        foreach (var proposal in invisibleProposals.Take(3)) // Test up to 3 to keep fast
        {
            // Attempt accept as Player
            var acceptResult = service.AcceptProposalAsync(
                new AcceptProposalCommand(proposal.Id, scenario.CampaignId, scenario.PlayerUserId, CampaignRole.Player),
                CancellationToken.None).GetAwaiter().GetResult();

            if (acceptResult.IsSuccess || acceptResult.Error!.StatusCode != 404)
            {
                allReturnNotFound = false;
                failureLabels.Add($"Accept proposal {proposal.Id}: expected 404, got {(acceptResult.IsSuccess ? "success" : acceptResult.Error!.StatusCode.ToString())}");
            }

            // Attempt reject as Player
            var rejectResult = service.RejectProposalAsync(
                new RejectProposalCommand(proposal.Id, scenario.CampaignId, scenario.PlayerUserId, CampaignRole.Player),
                CancellationToken.None).GetAwaiter().GetResult();

            if (rejectResult.IsSuccess || rejectResult.Error!.StatusCode != 404)
            {
                allReturnNotFound = false;
                failureLabels.Add($"Reject proposal {proposal.Id}: expected 404, got {(rejectResult.IsSuccess ? "success" : rejectResult.Error!.StatusCode.ToString())}");
            }

            // Attempt edit as Player
            var editResult = service.EditProposalAsync(
                new EditProposalCommand(proposal.Id, scenario.CampaignId, scenario.PlayerUserId, CampaignRole.Player,
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

    #region Property 4: Accept Transitions Status and Sets Metadata

    /// <summary>
    /// Property 4: Accept Transitions Status and Sets Metadata
    ///
    /// For any proposal with Status Pending or Edited that is accepted by an authorized reviewer,
    /// the proposal's Status SHALL transition to Accepted, ReviewedAt SHALL be set to approximately
    /// the current UTC timestamp, and ReviewedByUserId SHALL be set to the acting user's Id.
    ///
    /// **Validates: Requirements 2.1**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 4: Accept Transitions Status and Sets Metadata")]
    public Property Accept_transitions_status_and_sets_metadata(ProposalWithContext ctx)
    {
        var (service, proposalRepo, _, _, _, _) = CreateServiceWithRealApplicator(ctx);

        var before = DateTimeOffset.UtcNow;

        var command = new AcceptProposalCommand(
            ctx.Proposal.Id,
            ctx.CampaignId,
            ctx.OwnerUserId,
            CampaignRole.GM);

        var result = service.AcceptProposalAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        var after = DateTimeOffset.UtcNow;

        if (!result.IsSuccess)
        {
            return false.Label($"Accept failed unexpectedly: {result.Error!.Code} - {result.Error!.Message}");
        }

        var updatedProposal = proposalRepo.Proposals.First(p => p.Id == ctx.Proposal.Id);

        var statusCorrect = updatedProposal.Status == ReviewProposalStatus.Accepted;
        var reviewedAtSet = updatedProposal.ReviewedAt.HasValue
            && updatedProposal.ReviewedAt.Value >= before
            && updatedProposal.ReviewedAt.Value <= after;
        var reviewedByCorrect = updatedProposal.ReviewedByUserId == ctx.OwnerUserId;

        // Also verify result DTO matches
        var resultStatusCorrect = result.Value!.Status == ReviewProposalStatus.Accepted;
        var resultReviewedByCorrect = result.Value!.ReviewedByUserId == ctx.OwnerUserId;

        return statusCorrect
            .Label($"Proposal status should be Accepted, got {updatedProposal.Status}")
            .And(reviewedAtSet
                .Label("ReviewedAt should be set to approximately current UTC"))
            .And(reviewedByCorrect
                .Label($"ReviewedByUserId should be {ctx.OwnerUserId}, got {updatedProposal.ReviewedByUserId}"))
            .And(resultStatusCorrect
                .Label("Result DTO status should be Accepted"))
            .And(resultReviewedByCorrect
                .Label("Result DTO ReviewedByUserId should match acting user"));
    }

    #endregion

    #region Property 5: CreateArtifact Acceptance Creates Correct Artifact

    /// <summary>
    /// Property 5: CreateArtifact Acceptance Creates Correct Artifact
    ///
    /// For any valid CreateArtifact proposal with well-formed ProposedValueJson containing Name, Type,
    /// Summary, Visibility, and Confidence fields, acceptance SHALL create an Artifact with those field
    /// values, CampaignId from the ReviewBatch, Status Active, and CreatedAt/UpdatedAt set to the
    /// current UTC timestamp. The proposal's TargetId SHALL be updated to the newly created Artifact's Id.
    ///
    /// **Validates: Requirements 2.2, 9.1**
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 5: CreateArtifact Acceptance Creates Correct Artifact")]
    public Property CreateArtifact_acceptance_creates_correct_artifact(ProposalWithContext ctx)
    {
        var (service, proposalRepo, artifactRepo, sourceRefRepo, _, _) = CreateServiceWithRealApplicator(ctx);

        var before = DateTimeOffset.UtcNow;

        var command = new AcceptProposalCommand(
            ctx.Proposal.Id,
            ctx.CampaignId,
            ctx.OwnerUserId,
            CampaignRole.GM);

        var result = service.AcceptProposalAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        var after = DateTimeOffset.UtcNow;

        if (!result.IsSuccess)
        {
            return false.Label($"Accept failed: {result.Error!.Code} - {result.Error!.Message}");
        }

        // Parse the payload to get expected values
        var payload = JsonSerializer.Deserialize<CreateArtifactPayloadDto>(
            ctx.Proposal.ProposedValueJson, JsonOptions);

        if (payload is null)
            return false.Label("Failed to parse ProposedValueJson for assertion");

        // Find the created artifact
        var artifacts = artifactRepo.Artifacts;
        if (artifacts.Count != 1)
            return false.Label($"Expected exactly 1 artifact created, got {artifacts.Count}");

        var artifact = artifacts[0];

        // Verify proposal TargetId updated
        var updatedProposal = proposalRepo.Proposals.First(p => p.Id == ctx.Proposal.Id);
        var targetIdUpdated = updatedProposal.TargetId == artifact.Id;

        // Verify artifact fields match payload
        var nameCorrect = artifact.Name == payload.Name;

        var typeCorrect = Enum.TryParse<ArtifactType>(payload.Type, ignoreCase: true, out var expectedType)
            && artifact.Type == expectedType;

        var summaryCorrect = artifact.Summary == payload.Summary;

        // Visibility: uses payload value if valid, else defaults to source visibility
        VisibilityScope expectedVisibility;
        if (payload.Visibility is not null
            && Enum.TryParse<VisibilityScope>(payload.Visibility, ignoreCase: true, out var parsedVis))
        {
            expectedVisibility = parsedVis;
        }
        else
        {
            expectedVisibility = ctx.Source.Visibility;
        }
        var visibilityCorrect = artifact.Visibility == expectedVisibility;

        var confidenceCorrect = artifact.Confidence == payload.Confidence;
        var campaignIdCorrect = artifact.CampaignId == ctx.CampaignId;
        var statusCorrect = artifact.Status == ArtifactStatus.Active;

        var createdAtCorrect = artifact.CreatedAt >= before && artifact.CreatedAt <= after;
        var updatedAtCorrect = artifact.UpdatedAt >= before && artifact.UpdatedAt <= after;

        return targetIdUpdated
            .Label($"Proposal TargetId should be {artifact.Id}, got {updatedProposal.TargetId}")
            .And(nameCorrect
                .Label($"Artifact Name should be '{payload.Name}', got '{artifact.Name}'"))
            .And(typeCorrect
                .Label($"Artifact Type should be '{payload.Type}', got '{artifact.Type}'"))
            .And(summaryCorrect
                .Label($"Artifact Summary mismatch"))
            .And(visibilityCorrect
                .Label($"Artifact Visibility should be {expectedVisibility}, got {artifact.Visibility}"))
            .And(confidenceCorrect
                .Label($"Artifact Confidence should be {payload.Confidence}, got {artifact.Confidence}"))
            .And(campaignIdCorrect
                .Label($"Artifact CampaignId should be {ctx.CampaignId}, got {artifact.CampaignId}"))
            .And(statusCorrect
                .Label($"Artifact Status should be Active, got {artifact.Status}"))
            .And(createdAtCorrect
                .Label("Artifact CreatedAt should be approximately current UTC"))
            .And(updatedAtCorrect
                .Label("Artifact UpdatedAt should be approximately current UTC"));
    }

    /// <summary>
    /// DTO for deserializing CreateArtifact payloads in assertions.
    /// </summary>
    private record CreateArtifactPayloadDto(
        string Name,
        string Type,
        string? Summary,
        string? Visibility,
        decimal? Confidence);

    #endregion
}
