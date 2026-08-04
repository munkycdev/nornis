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
/// The two paths that must not touch the knowledge graph — rejecting, and editing a proposal's JSON —
/// and the one thing an edit must still allow afterwards: accepting or rejecting the edited version.
/// </summary>
[TestFixture]
[Category("Feature: review-proposal-workflow")]
public class ProposalRejectionAndEditProperties
{
    #region Property 10: Reject Transitions Without Knowledge Graph Changes

    /// <summary>
    /// Property 10: Reject Transitions Without Knowledge Graph Changes
    ///
    /// For any proposal with Status Pending or Edited that is rejected by an authorized reviewer,
    /// the proposal's Status SHALL transition to Rejected, ReviewedAt and ReviewedByUserId SHALL
    /// be set, and no Artifact, ArtifactFact, ArtifactRelationship, or SourceReference records
    /// SHALL be created or modified.
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 10: Reject Transitions Without Knowledge Graph Changes")]
    public Property Reject_transitions_without_knowledge_graph_changes(ReviewChangeType changeType)
    {
        var fakeCtx = ReviewHarness.WithFakeApplicator();

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
        fakeCtx.SourceRepo.Seed(source);
        fakeCtx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // Use random initial status (Pending or Edited)
        var initialStatus = changeType.GetHashCode() % 2 == 0
            ? ReviewProposalStatus.Pending
            : ReviewProposalStatus.Edited;

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = changeType,
            TargetType = ReviewTargetType.Artifact,
            TargetId = Guid.NewGuid(),
            ProposedValueJson = "{\"name\":\"Test\",\"type\":\"Character\"}",
            Rationale = "Test rationale",
            Confidence = 0.8m,
            Status = initialStatus,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        };
        fakeCtx.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();

        var before = DateTimeOffset.UtcNow;
        var result = fakeCtx.Service.RejectProposalAsync(
            new RejectProposalCommand(proposal.Id, worldId, userId, WorldRole.GM),
            CancellationToken.None).GetAwaiter().GetResult();
        var after = DateTimeOffset.UtcNow;

        if (!result.IsSuccess)
            return false.Label($"Reject failed: {result.Error!.Code} - {result.Error!.Message}");

        var updatedProposal = fakeCtx.ProposalRepo.Proposals.First(p => p.Id == proposal.Id);

        var statusRejected = updatedProposal.Status == ReviewProposalStatus.Rejected;
        var reviewedAtSet = updatedProposal.ReviewedAt.HasValue
            && updatedProposal.ReviewedAt.Value >= before
            && updatedProposal.ReviewedAt.Value <= after;
        var reviewedBySet = updatedProposal.ReviewedByUserId == userId;

        // No knowledge graph changes
        var noArtifacts = fakeCtx.ArtifactRepo.Artifacts.Count == 0;
        var noFacts = fakeCtx.ArtifactFactRepo.Facts.Count == 0;
        var noRelationships = fakeCtx.ArtifactRelationshipRepo.Relationships.Count == 0;
        var noSourceRefs = fakeCtx.SourceRefRepo.References.Count == 0;

        return statusRejected.Label($"Status should be Rejected, got {updatedProposal.Status}")
            .And(reviewedAtSet.Label("ReviewedAt should be set to approximately current UTC"))
            .And(reviewedBySet.Label($"ReviewedByUserId should be {userId}"))
            .And(noArtifacts.Label($"No artifacts should be created, got {fakeCtx.ArtifactRepo.Artifacts.Count}"))
            .And(noFacts.Label($"No facts should be created, got {fakeCtx.ArtifactFactRepo.Facts.Count}"))
            .And(noRelationships.Label($"No relationships should be created, got {fakeCtx.ArtifactRelationshipRepo.Relationships.Count}"))
            .And(noSourceRefs.Label($"No source refs should be created, got {fakeCtx.SourceRefRepo.References.Count}"));
    }

    #endregion

    #region Property 11: Edit Replaces JSON Without Mutating Knowledge Graph

    /// <summary>
    /// Property 11: Edit Replaces JSON Without Mutating Knowledge Graph
    ///
    /// For any proposal with Status Pending or Edited, a valid edit request SHALL replace
    /// the entire ProposedValueJson with the submitted value, transition Status to Edited,
    /// set ReviewedAt and ReviewedByUserId, and SHALL NOT create or modify any Artifact,
    /// ArtifactFact, ArtifactRelationship, or SourceReference.
    /// </summary>
    [FsCheck.NUnit.Property(Arbitrary = [typeof(ReviewArbitraries)], MaxTest = 100)]
    [Description("Feature: review-proposal-workflow, Property 11: Edit Replaces JSON Without Mutating Knowledge Graph")]
    public Property Edit_replaces_json_without_mutating_knowledge_graph(ReviewProposalStatus initialStatus)
    {
        // Only test Pending or Edited initial statuses
        if (initialStatus is not (ReviewProposalStatus.Pending or ReviewProposalStatus.Edited))
            return true.ToProperty();

        var ctx = ReviewHarness.WithFakeApplicator();

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
    /// </summary>
    [Test]
    [Description("Feature: review-proposal-workflow, Property 12: Edited Proposals Allow Subsequent Accept")]
    public void Edited_proposals_allow_subsequent_accept()
    {
        // Use REAL applicator for accept — the edited JSON must actually work
        var ctx = ReviewHarness.WithRealApplicator();

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
            Assert.Fail($"Accept of edited proposal failed: {result.Error!.Code} - {result.Error!.Message}");

        var updated = ctx.ProposalRepo.Proposals.First(p => p.Id == proposal.Id);
        var statusAccepted = updated.Status == ReviewProposalStatus.Accepted;
        var artifactCreated = ctx.ArtifactRepo.Artifacts.Count == 1;

        Assert.Multiple(() =>
        {
            Assert.That(statusAccepted, Is.True, $"Status should be Accepted, got {updated.Status}");
            Assert.That(artifactCreated, Is.True, $"Artifact should be created, got {ctx.ArtifactRepo.Artifacts.Count}");
        });
    }

    [Test]
    [Description("Feature: review-proposal-workflow, Property 12: Edited Proposals Allow Subsequent Reject")]
    public void Edited_proposals_allow_subsequent_reject()
    {
        // Use FAKE applicator for reject — no application needed
        var ctx = ReviewHarness.WithFakeApplicator();

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
            Assert.Fail($"Reject of edited proposal failed: {result.Error!.Code} - {result.Error!.Message}");

        var updated = ctx.ProposalRepo.Proposals.First(p => p.Id == proposal.Id);
        var statusRejected = updated.Status == ReviewProposalStatus.Rejected;
        var reviewedAtSet = updated.ReviewedAt.HasValue
            && updated.ReviewedAt.Value >= before
            && updated.ReviewedAt.Value <= after;
        var noArtifacts = ctx.ArtifactRepo.Artifacts.Count == 0;

        Assert.Multiple(() =>
        {
            Assert.That(statusRejected, Is.True, $"Status should be Rejected, got {updated.Status}");
            Assert.That(reviewedAtSet, Is.True, "ReviewedAt should be updated");
            Assert.That(noArtifacts, Is.True, "No artifacts should be created on reject");
        });
    }

    #endregion
}
