using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Exercises the ordered world wipe against a real relational provider (SQLite with FK
/// enforcement on), so a violated Restrict/NoAction constraint or a wrong delete order
/// fails here rather than in production. Two identical world graphs are seeded; deleting
/// one must leave zero trace of it and the other fully intact.
/// </summary>
[TestFixture]
public class WorldRepositoryDeleteTests : IntegrationTestBase
{
    /// <summary>Every row id seeded for one world, for pinpoint gone/intact assertions.</summary>
    private sealed record WorldGraph(
        Guid UserId, Guid WorldId, Guid MemberId, Guid InviteId, Guid CampaignId,
        Guid ArtifactId, Guid StorylineId, Guid CharacterId, Guid CampaignCharacterId,
        Guid StorylineCampaignId, Guid FactId, Guid RelationshipId, Guid SourceId,
        Guid ExtractionId, Guid ReferenceId, Guid AttachmentId, Guid PlacemarkId,
        Guid BatchId, Guid ProposalId, Guid UsageId, Guid AssessmentId, Guid FindingId,
        Guid DismissalId, Guid DocumentId, Guid ChunkId, Guid ReplayId);

    /// <summary>
    /// Seeds a world exercising every table and every awkward FK: a character linked to an
    /// artifact (NoAction), a source in a campaign (Restrict), a review batch on the source
    /// (Restrict), usage-ledger rows pointing at source and batch (NoAction), and the
    /// join/satellite tables that carry no WorldId of their own.
    /// </summary>
    private WorldGraph SeedWorldGraph()
    {
        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = $"World {tag}",
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var member = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            UserId = user.Id,
            Role = WorldRole.GM,
            JoinedAt = now,
        };
        var invite = new WorldInvite
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Code = Guid.NewGuid().ToString("N"),
            Role = WorldRole.Player,
            CreatedByUserId = user.Id,
            CreatedAt = now,
        };
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Name = "First Campaign",
            Status = CampaignStatus.Active,
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Type = ArtifactType.Character,
            Name = "Tavrin",
            Visibility = VisibilityScope.GMOnly,
            Status = ArtifactStatus.Active,
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var storyline = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Type = ArtifactType.Storyline,
            Name = "Missing Caravan",
            Visibility = VisibilityScope.GMOnly,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var character = new Character
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            WorldMemberId = member.Id,
            Name = "Tavrin",
            ArtifactId = artifact.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var campaignCharacter = new CampaignCharacter
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            CharacterId = character.Id,
            CreatedAt = now,
        };
        var storylineCampaign = new StorylineCampaign
        {
            Id = Guid.NewGuid(),
            ArtifactId = storyline.Id,
            CampaignId = campaign.Id,
            CreatedByUserId = user.Id,
            CreatedAt = now,
        };
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = "location",
            Value = "Black Harbor",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.GMOnly,
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            ArtifactAId = artifact.Id,
            ArtifactBId = storyline.Id,
            Type = "SuspectedIn",
            TruthState = TruthState.Likely,
            Visibility = VisibilityScope.GMOnly,
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            CampaignId = campaign.Id,
            Type = SourceType.SessionNote,
            Title = "Session 1",
            CreatedByUserId = user.Id,
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = now,
        };
        var extraction = new SourceExtraction
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            ExtractionType = SourceExtractionType.Manual,
            Text = "The caravan vanished.",
            CreatedAt = now,
        };
        var reference = new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TargetType = SourceReferenceTargetType.Artifact,
            TargetId = artifact.Id,
            CreatedAt = now,
        };
        var attachment = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = world.Id,
            Kind = SourceAttachmentKind.PageImage,
            FileName = "map.png",
            ContentType = "image/png",
            SizeBytes = 42,
            BlobPath = $"worlds/{world.Id}/sources/{source.Id}/map.png",
            Status = SourceAttachmentStatus.PendingUpload,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var placemark = new MapPlacemark
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            SourceAttachmentId = attachment.Id,
            ArtifactId = artifact.Id,
            X = 0.5m,
            Y = 0.5m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = now,
        };
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = "{\"name\":\"Tavrin\"}",
            Status = ReviewProposalStatus.Accepted,
            ReviewedByUserId = user.Id,
            CreatedAt = now,
        };
        var usage = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            UserId = user.Id,
            OperationType = AiOperationType.SourceExtraction,
            Model = "gpt-4o",
            InputTokens = 100,
            OutputTokens = 50,
            TotalTokens = 150,
            EstimatedCostUsd = 0.01m,
            SourceId = source.Id,
            ReviewBatchId = batch.Id,
            DurationMs = 1200,
            Succeeded = true,
            CreatedAt = now,
        };
        var assessment = new HealthAssessment
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Model = "gpt-4o",
            Score = 80,
            CreatedAt = now,
        };
        var finding = new ContinuityFinding
        {
            Id = Guid.NewGuid(),
            HealthAssessmentId = assessment.Id,
            Category = ContinuityFindingCategory.Contradiction,
            Severity = ContinuityFindingSeverity.High,
            Summary = "Tavrin is in two places at once.",
            ArtifactId = artifact.Id,
            Status = ContinuityFindingStatus.Open,
        };
        var dismissal = new ContinuityDismissal
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Category = ContinuityFindingCategory.DanglingThread,
            EvidenceJson = $"[\"artifact:{artifact.Id}\"]",
            DismissedAtUtc = now,
        };
        var document = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Title = "Sourcebook",
            FileName = "book.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            BlobPath = $"worlds/{world.Id}/library/{Guid.NewGuid()}/book.pdf",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = VisibilityScope.GMOnly,
            Status = LibraryDocumentStatus.Indexed,
            UploadedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var chunk = new LibraryChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            WorldId = world.Id,
            Ord = 0,
            Page = 1,
            Text = "In the beginning...",
            CreatedAt = now,
        };
        var replay = new ExtractionReplay
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            CurrentSourceId = source.Id,
            Status = ExtractionReplayStatus.Completed,
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = now,
        };

        Context.AddRange(
            user, world, member, invite, campaign, artifact, storyline, character,
            campaignCharacter, storylineCampaign, fact, relationship, source, extraction,
            reference, attachment, placemark, batch, proposal, usage, assessment, finding,
            dismissal, document, chunk, replay);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();

        return new WorldGraph(
            user.Id, world.Id, member.Id, invite.Id, campaign.Id, artifact.Id, storyline.Id,
            character.Id, campaignCharacter.Id, storylineCampaign.Id, fact.Id, relationship.Id,
            source.Id, extraction.Id, reference.Id, attachment.Id, placemark.Id, batch.Id,
            proposal.Id, usage.Id, assessment.Id, finding.Id, dismissal.Id, document.Id,
            chunk.Id, replay.Id);
    }

    /// <summary>Counts how many of the graph's seeded rows still exist, table by table.</summary>
    private async Task<int> CountRemainingRowsAsync(WorldGraph g)
    {
        var remaining = 0;
        remaining += await Context.Worlds.CountAsync(x => x.Id == g.WorldId);
        remaining += await Context.WorldMembers.CountAsync(x => x.Id == g.MemberId);
        remaining += await Context.WorldInvites.CountAsync(x => x.Id == g.InviteId);
        remaining += await Context.Campaigns.CountAsync(x => x.Id == g.CampaignId);
        remaining += await Context.Artifacts.CountAsync(x => x.Id == g.ArtifactId || x.Id == g.StorylineId);
        remaining += await Context.Characters.CountAsync(x => x.Id == g.CharacterId);
        remaining += await Context.CampaignCharacters.CountAsync(x => x.Id == g.CampaignCharacterId);
        remaining += await Context.StorylineCampaigns.CountAsync(x => x.Id == g.StorylineCampaignId);
        remaining += await Context.ArtifactFacts.CountAsync(x => x.Id == g.FactId);
        remaining += await Context.ArtifactRelationships.CountAsync(x => x.Id == g.RelationshipId);
        remaining += await Context.Sources.CountAsync(x => x.Id == g.SourceId);
        remaining += await Context.SourceExtractions.CountAsync(x => x.Id == g.ExtractionId);
        remaining += await Context.SourceReferences.CountAsync(x => x.Id == g.ReferenceId);
        remaining += await Context.SourceAttachments.CountAsync(x => x.Id == g.AttachmentId);
        remaining += await Context.MapPlacemarks.CountAsync(x => x.Id == g.PlacemarkId);
        remaining += await Context.ReviewBatches.CountAsync(x => x.Id == g.BatchId);
        remaining += await Context.ReviewProposals.CountAsync(x => x.Id == g.ProposalId);
        remaining += await Context.AiUsageRecords.CountAsync(x => x.Id == g.UsageId);
        remaining += await Context.HealthAssessments.CountAsync(x => x.Id == g.AssessmentId);
        remaining += await Context.ContinuityFindings.CountAsync(x => x.Id == g.FindingId);
        remaining += await Context.ContinuityDismissals.CountAsync(x => x.Id == g.DismissalId);
        remaining += await Context.LibraryDocuments.CountAsync(x => x.Id == g.DocumentId);
        remaining += await Context.LibraryChunks.CountAsync(x => x.Id == g.ChunkId);
        remaining += await Context.ExtractionReplays.CountAsync(x => x.Id == g.ReplayId);
        return remaining;
    }

    /// <summary>The number of rows one seeded graph contributes (2 artifacts, 1 of everything else).</summary>
    private const int SeededRowCount = 25;

    [Test]
    public async Task DeleteAsync_RemovesEveryRowOfTheWorld_AndNothingElse()
    {
        var doomed = SeedWorldGraph();
        var survivor = SeedWorldGraph();
        var sut = new WorldRepository(Context);

        await sut.DeleteAsync(doomed.WorldId);
        Context.ChangeTracker.Clear();

        Assert.That(await CountRemainingRowsAsync(doomed), Is.Zero,
            "every row of the deleted world must be gone");
        Assert.That(await CountRemainingRowsAsync(survivor), Is.EqualTo(SeededRowCount),
            "the other world's graph must be fully intact");

        // The usage ledger is hard-deleted, not orphaned via SetNull.
        Assert.That(await Context.AiUsageRecords.CountAsync(x => x.Id == doomed.UsageId || x.WorldId == null), Is.Zero);

        // Users are global and must never die with a world.
        Assert.That(await Context.Users.CountAsync(u => u.Id == doomed.UserId), Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteAsync_UnknownWorld_IsANoOp()
    {
        var graph = SeedWorldGraph();
        var sut = new WorldRepository(Context);

        await sut.DeleteAsync(Guid.NewGuid());

        Assert.That(await CountRemainingRowsAsync(graph), Is.EqualTo(SeededRowCount));
    }
}
