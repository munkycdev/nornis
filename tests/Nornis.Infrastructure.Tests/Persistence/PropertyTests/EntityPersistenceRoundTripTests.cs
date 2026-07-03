using FsCheck;
using FsCheck.NUnit;
using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence.PropertyTests;

/// <summary>
/// Property-based tests verifying that all domain entities can be persisted and
/// retrieved with all scalar properties (including enum values) intact.
/// 
/// **Validates: Requirements 5.4, 7.4**
/// </summary>
[TestFixture]
public class EntityPersistenceRoundTripTests : IntegrationTestBase
{
    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void User_RoundTrip_AllPropertiesMatch(User generated)
    {
        // Assign a unique Id and Auth0SubjectId to avoid collisions across iterations
        generated.Id = Guid.NewGuid();
        generated.Auth0SubjectId = $"auth0|{Guid.NewGuid():N}";
        generated.RowVersion = [];

        // Persist
        Context.Users.Add(generated);
        Context.SaveChanges();

        // Retrieve from fresh context
        using var readContext = CreateNewContext();
        var retrieved = readContext.Users.AsNoTracking().First(u => u.Id == generated.Id);

        // Assert all scalar properties
        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.Auth0SubjectId, Is.EqualTo(generated.Auth0SubjectId));
        Assert.That(retrieved.Username, Is.EqualTo(generated.Username));
        Assert.That(retrieved.Email, Is.EqualTo(generated.Email));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
        Assert.That(retrieved.UpdatedAt, Is.EqualTo(generated.UpdatedAt));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void Campaign_RoundTrip_AllPropertiesMatch(Campaign generated)
    {
        // Create parent User
        var user = CreateUser();

        generated.Id = Guid.NewGuid();
        generated.CreatedByUserId = user.Id;
        generated.RowVersion = [];

        Context.Campaigns.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.Campaigns.AsNoTracking().First(c => c.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.Name, Is.EqualTo(generated.Name));
        Assert.That(retrieved.Description, Is.EqualTo(generated.Description));
        Assert.That(retrieved.GameSystem, Is.EqualTo(generated.GameSystem));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
        Assert.That(retrieved.UpdatedAt, Is.EqualTo(generated.UpdatedAt));
        Assert.That(retrieved.CreatedByUserId, Is.EqualTo(generated.CreatedByUserId));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void CampaignMember_RoundTrip_AllPropertiesMatch(CampaignMember generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);

        generated.Id = Guid.NewGuid();
        generated.CampaignId = campaign.Id;
        generated.UserId = user.Id;

        Context.CampaignMembers.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.CampaignMembers.AsNoTracking().First(cm => cm.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.CampaignId, Is.EqualTo(generated.CampaignId));
        Assert.That(retrieved.UserId, Is.EqualTo(generated.UserId));
        Assert.That(retrieved.Role, Is.EqualTo(generated.Role));
        Assert.That(retrieved.DisplayName, Is.EqualTo(generated.DisplayName));
        Assert.That(retrieved.CharacterName, Is.EqualTo(generated.CharacterName));
        Assert.That(retrieved.JoinedAt, Is.EqualTo(generated.JoinedAt));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void Source_RoundTrip_AllPropertiesMatch(Source generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);

        generated.Id = Guid.NewGuid();
        generated.CampaignId = campaign.Id;
        generated.CreatedByUserId = user.Id;

        Context.Sources.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.Sources.AsNoTracking().First(s => s.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.CampaignId, Is.EqualTo(generated.CampaignId));
        Assert.That(retrieved.Type, Is.EqualTo(generated.Type));
        Assert.That(retrieved.Title, Is.EqualTo(generated.Title));
        Assert.That(retrieved.Body, Is.EqualTo(generated.Body));
        Assert.That(retrieved.Uri, Is.EqualTo(generated.Uri));
        Assert.That(retrieved.OccurredAt, Is.EqualTo(generated.OccurredAt));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
        Assert.That(retrieved.CreatedByUserId, Is.EqualTo(generated.CreatedByUserId));
        Assert.That(retrieved.Visibility, Is.EqualTo(generated.Visibility));
        Assert.That(retrieved.ProcessingStatus, Is.EqualTo(generated.ProcessingStatus));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void SourceExtraction_RoundTrip_AllPropertiesMatch(SourceExtraction generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);
        var source = CreateSource(campaign.Id, user.Id);

        generated.Id = Guid.NewGuid();
        generated.SourceId = source.Id;

        Context.SourceExtractions.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.SourceExtractions.AsNoTracking().First(se => se.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.SourceId, Is.EqualTo(generated.SourceId));
        Assert.That(retrieved.ExtractionType, Is.EqualTo(generated.ExtractionType));
        Assert.That(retrieved.Text, Is.EqualTo(generated.Text));
        Assert.That(retrieved.Confidence, Is.EqualTo(generated.Confidence));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void Artifact_RoundTrip_AllPropertiesMatch(Artifact generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);

        generated.Id = Guid.NewGuid();
        generated.CampaignId = campaign.Id;
        generated.RowVersion = [];

        Context.Artifacts.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.Artifacts.AsNoTracking().First(a => a.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.CampaignId, Is.EqualTo(generated.CampaignId));
        Assert.That(retrieved.Type, Is.EqualTo(generated.Type));
        Assert.That(retrieved.Name, Is.EqualTo(generated.Name));
        Assert.That(retrieved.Summary, Is.EqualTo(generated.Summary));
        Assert.That(retrieved.Visibility, Is.EqualTo(generated.Visibility));
        Assert.That(retrieved.Confidence, Is.EqualTo(generated.Confidence));
        Assert.That(retrieved.Status, Is.EqualTo(generated.Status));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
        Assert.That(retrieved.UpdatedAt, Is.EqualTo(generated.UpdatedAt));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void ArtifactFact_RoundTrip_AllPropertiesMatch(ArtifactFact generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);
        var artifact = CreateArtifact(campaign.Id);

        generated.Id = Guid.NewGuid();
        generated.ArtifactId = artifact.Id;
        generated.RowVersion = [];

        Context.ArtifactFacts.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.ArtifactFacts.AsNoTracking().First(af => af.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.ArtifactId, Is.EqualTo(generated.ArtifactId));
        Assert.That(retrieved.Predicate, Is.EqualTo(generated.Predicate));
        Assert.That(retrieved.Value, Is.EqualTo(generated.Value));
        Assert.That(retrieved.Confidence, Is.EqualTo(generated.Confidence));
        Assert.That(retrieved.TruthState, Is.EqualTo(generated.TruthState));
        Assert.That(retrieved.Visibility, Is.EqualTo(generated.Visibility));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
        Assert.That(retrieved.UpdatedAt, Is.EqualTo(generated.UpdatedAt));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void ArtifactRelationship_RoundTrip_AllPropertiesMatch(ArtifactRelationship generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);
        var artifactA = CreateArtifact(campaign.Id);
        var artifactB = CreateArtifact(campaign.Id);

        generated.Id = Guid.NewGuid();
        generated.CampaignId = campaign.Id;
        generated.ArtifactAId = artifactA.Id;
        generated.ArtifactBId = artifactB.Id;
        generated.RowVersion = [];

        Context.ArtifactRelationships.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.ArtifactRelationships.AsNoTracking().First(ar => ar.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.CampaignId, Is.EqualTo(generated.CampaignId));
        Assert.That(retrieved.ArtifactAId, Is.EqualTo(generated.ArtifactAId));
        Assert.That(retrieved.ArtifactBId, Is.EqualTo(generated.ArtifactBId));
        Assert.That(retrieved.Type, Is.EqualTo(generated.Type));
        Assert.That(retrieved.Description, Is.EqualTo(generated.Description));
        Assert.That(retrieved.Confidence, Is.EqualTo(generated.Confidence));
        Assert.That(retrieved.TruthState, Is.EqualTo(generated.TruthState));
        Assert.That(retrieved.Visibility, Is.EqualTo(generated.Visibility));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
        Assert.That(retrieved.UpdatedAt, Is.EqualTo(generated.UpdatedAt));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void SourceReference_RoundTrip_AllPropertiesMatch(SourceReference generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);
        var source = CreateSource(campaign.Id, user.Id);

        generated.Id = Guid.NewGuid();
        generated.SourceId = source.Id;

        Context.SourceReferences.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.SourceReferences.AsNoTracking().First(sr => sr.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.SourceId, Is.EqualTo(generated.SourceId));
        Assert.That(retrieved.TargetType, Is.EqualTo(generated.TargetType));
        Assert.That(retrieved.TargetId, Is.EqualTo(generated.TargetId));
        Assert.That(retrieved.Quote, Is.EqualTo(generated.Quote));
        Assert.That(retrieved.Notes, Is.EqualTo(generated.Notes));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void ReviewBatch_RoundTrip_AllPropertiesMatch(ReviewBatch generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);
        var source = CreateSource(campaign.Id, user.Id);

        generated.Id = Guid.NewGuid();
        generated.CampaignId = campaign.Id;
        generated.SourceId = source.Id;

        Context.ReviewBatches.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.ReviewBatches.AsNoTracking().First(rb => rb.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.CampaignId, Is.EqualTo(generated.CampaignId));
        Assert.That(retrieved.SourceId, Is.EqualTo(generated.SourceId));
        Assert.That(retrieved.Status, Is.EqualTo(generated.Status));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
        Assert.That(retrieved.CompletedAt, Is.EqualTo(generated.CompletedAt));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void ReviewProposal_RoundTrip_AllPropertiesMatch(ReviewProposal generated)
    {
        var user = CreateUser();
        var campaign = CreateCampaign(user.Id);
        var source = CreateSource(campaign.Id, user.Id);
        var reviewBatch = CreateReviewBatch(campaign.Id, source.Id);

        generated.Id = Guid.NewGuid();
        generated.ReviewBatchId = reviewBatch.Id;
        generated.ReviewedByUserId = null; // Nullable FK — set to null to avoid FK constraint
        generated.RowVersion = [];

        Context.ReviewProposals.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.ReviewProposals.AsNoTracking().First(rp => rp.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.ReviewBatchId, Is.EqualTo(generated.ReviewBatchId));
        Assert.That(retrieved.ChangeType, Is.EqualTo(generated.ChangeType));
        Assert.That(retrieved.TargetType, Is.EqualTo(generated.TargetType));
        Assert.That(retrieved.TargetId, Is.EqualTo(generated.TargetId));
        Assert.That(retrieved.ProposedValueJson, Is.EqualTo(generated.ProposedValueJson));
        Assert.That(retrieved.Rationale, Is.EqualTo(generated.Rationale));
        Assert.That(retrieved.Confidence, Is.EqualTo(generated.Confidence));
        Assert.That(retrieved.Status, Is.EqualTo(generated.Status));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
        Assert.That(retrieved.ReviewedAt, Is.EqualTo(generated.ReviewedAt));
        Assert.That(retrieved.ReviewedByUserId, Is.EqualTo(generated.ReviewedByUserId));
    }

    [FsCheck.NUnit.Property(Arbitrary = [typeof(EntityGenerators.DomainArbitraries)], MaxTest = 100)]
    public void AiUsageRecord_RoundTrip_AllPropertiesMatch(AiUsageRecord generated)
    {
        // AiUsageRecord has all nullable FKs — set them to null to avoid constraint issues
        generated.Id = Guid.NewGuid();
        generated.CampaignId = null;
        generated.UserId = null;
        generated.SourceId = null;
        generated.ReviewBatchId = null;

        Context.AiUsageRecords.Add(generated);
        Context.SaveChanges();

        using var readContext = CreateNewContext();
        var retrieved = readContext.AiUsageRecords.AsNoTracking().First(r => r.Id == generated.Id);

        Assert.That(retrieved.Id, Is.EqualTo(generated.Id));
        Assert.That(retrieved.CampaignId, Is.EqualTo(generated.CampaignId));
        Assert.That(retrieved.UserId, Is.EqualTo(generated.UserId));
        Assert.That(retrieved.OperationType, Is.EqualTo(generated.OperationType));
        Assert.That(retrieved.Model, Is.EqualTo(generated.Model));
        Assert.That(retrieved.InputTokens, Is.EqualTo(generated.InputTokens));
        Assert.That(retrieved.OutputTokens, Is.EqualTo(generated.OutputTokens));
        Assert.That(retrieved.TotalTokens, Is.EqualTo(generated.TotalTokens));
        Assert.That(retrieved.EstimatedCostUsd, Is.EqualTo(generated.EstimatedCostUsd));
        Assert.That(retrieved.SourceId, Is.EqualTo(generated.SourceId));
        Assert.That(retrieved.ReviewBatchId, Is.EqualTo(generated.ReviewBatchId));
        Assert.That(retrieved.DurationMs, Is.EqualTo(generated.DurationMs));
        Assert.That(retrieved.Succeeded, Is.EqualTo(generated.Succeeded));
        Assert.That(retrieved.ErrorCode, Is.EqualTo(generated.ErrorCode));
        Assert.That(retrieved.CreatedAt, Is.EqualTo(generated.CreatedAt));
    }

    #region Helper Methods

    private User CreateUser()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{Guid.NewGuid():N}",
            Username = "TestUser",
            Email = "test@nornis.app",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        };
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    private Campaign CreateCampaign(Guid userId)
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = "Black Harbor Investigation",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            RowVersion = []
        };
        Context.Campaigns.Add(campaign);
        Context.SaveChanges();
        return campaign;
    }

    private Source CreateSource(Guid campaignId, Guid userId)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            Type = SourceType.SessionNote,
            Title = "Session 1 Notes",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        Context.Sources.Add(source);
        Context.SaveChanges();
        return source;
    }

    private Artifact CreateArtifact(Guid campaignId)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        };
        Context.Artifacts.Add(artifact);
        Context.SaveChanges();
        return artifact;
    }

    private ReviewBatch CreateReviewBatch(Guid campaignId, Guid sourceId)
    {
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            SourceId = sourceId,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Context.ReviewBatches.Add(batch);
        Context.SaveChanges();
        return batch;
    }

    #endregion
}
