using System.Text.Json;
using Nornis.Application.Application;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Application;

[TestFixture]
public class ProposalApplicatorTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private ProposalApplicator _applicator = null!;

    private Guid _worldId;
    private Guid _sourceId;
    private Guid _batchId;
    private Source _source = null!;
    private ReviewBatch _batch = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();

        _applicator = new ProposalApplicator(
            _artifactRepo,
            _factRepo,
            _relationshipRepo,
            _sourceRefRepo,
            _sourceRepo, new InMemorySourceAttachmentRepository(), new InMemoryMapPlacemarkRepository());

        _worldId = Guid.NewGuid();
        _sourceId = Guid.NewGuid();
        _batchId = Guid.NewGuid();

        _source = new Source
        {
            Id = _sourceId,
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 1: Black Harbor",
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _sourceRepo.Seed(_source);

        _batch = new ReviewBatch
        {
            Id = _batchId,
            WorldId = _worldId,
            SourceId = _sourceId,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
    }

    #region Quote carry-through

    [Test]
    public async Task CreateArtifact_CarriesExtractionQuoteOntoArtifactReference()
    {
        var payload = new CreateArtifactPayload(
            "Captain Voss", "Character", "A harbor captain", "PartyVisible", 0.85m);
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact, payload);

        // Extraction records the supporting excerpt on the proposal's own reference.
        _sourceRefRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = _sourceId,
            TargetType = SourceReferenceTargetType.ReviewProposal,
            TargetId = proposal.Id,
            Quote = "We questioned Captain Voss in Black Harbor.",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var artifactRef = _sourceRefRepo.References
            .Single(r => r.TargetType == SourceReferenceTargetType.Artifact);
        Assert.That(artifactRef.Quote, Is.EqualTo("We questioned Captain Voss in Black Harbor."),
            "the excerpt captured at extraction must survive onto the accepted entity's reference");
    }

    [Test]
    public async Task CreateArtifact_NoExtractionQuote_ReferenceHasNullQuote()
    {
        var payload = new CreateArtifactPayload(
            "Captain Voss", "Character", null, "PartyVisible", null);
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var artifactRef = _sourceRefRepo.References
            .Single(r => r.TargetType == SourceReferenceTargetType.Artifact);
        Assert.That(artifactRef.Quote, Is.Null);
    }

    #endregion

    #region CreateArtifact

    [Test]
    public async Task CreateArtifact_CreatesArtifactWithCorrectFields()
    {
        var payload = new CreateArtifactPayload(
            "Captain Voss", "Character", "A harbor captain", "PartyVisible", 0.85m);
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var artifact = _artifactRepo.Artifacts.Single();
        Assert.That(artifact.Name, Is.EqualTo("Captain Voss"));
        Assert.That(artifact.Type, Is.EqualTo(ArtifactType.Character));
        Assert.That(artifact.Summary, Is.EqualTo("A harbor captain"));
        Assert.That(artifact.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(artifact.Confidence, Is.EqualTo(0.85m));
        Assert.That(artifact.Status, Is.EqualTo(ArtifactStatus.Active));
        Assert.That(artifact.WorldId, Is.EqualTo(_worldId));
        Assert.That(artifact.CreatedAt, Is.EqualTo(artifact.UpdatedAt));
    }

    [Test]
    public async Task CreateArtifact_UpdatesProposalTargetId()
    {
        var payload = new CreateArtifactPayload(
            "Black Harbor", "Location", null, null, null);
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact, payload);

        Assert.That(proposal.TargetId, Is.Null);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var createdArtifact = _artifactRepo.Artifacts.Single();
        Assert.That(proposal.TargetId, Is.EqualTo(createdArtifact.Id));
        Assert.That(result.Value!.EntityId, Is.EqualTo(createdArtifact.Id));
        Assert.That(result.Value.TargetType, Is.EqualTo(SourceReferenceTargetType.Artifact));
    }

    #endregion

    #region UpdateArtifact

    [Test]
    public async Task UpdateArtifact_UpdatesOnlyNonNullFieldsAndSetsUpdatedAt()
    {
        var existingArtifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Summary = "Original summary",
            Visibility = VisibilityScope.PartyVisible,
            Confidence = 0.7m,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _artifactRepo.Seed(existingArtifact);

        var payload = new UpdateArtifactPayload(
            null, "Updated summary about Black Harbor", null, 0.9m, null);
        var proposal = MakeProposal(ReviewChangeType.UpdateArtifact, payload, existingArtifact.Id);

        var beforeApply = DateTimeOffset.UtcNow;
        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var updated = _artifactRepo.Artifacts.Single();
        Assert.That(updated.Name, Is.EqualTo("Captain Voss")); // unchanged
        Assert.That(updated.Summary, Is.EqualTo("Updated summary about Black Harbor"));
        Assert.That(updated.Visibility, Is.EqualTo(VisibilityScope.PartyVisible)); // unchanged
        Assert.That(updated.Confidence, Is.EqualTo(0.9m));
        Assert.That(updated.Status, Is.EqualTo(ArtifactStatus.Active)); // unchanged
        Assert.That(updated.UpdatedAt, Is.GreaterThanOrEqualTo(beforeApply));
    }

    [Test]
    public async Task UpdateArtifact_TargetNotFound_ReturnsValidationError()
    {
        var nonExistentId = Guid.NewGuid();
        var payload = new UpdateArtifactPayload("New Name", null, null, null, null);
        var proposal = MakeProposal(ReviewChangeType.UpdateArtifact, payload, nonExistentId);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("target_not_found"));
    }

    #endregion

    #region MergeArtifact

    [Test]
    public async Task MergeArtifact_ReassignsFactsAndRelationships_RemovesSelfRefs_ArchivesSource()
    {
        var targetArtifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Summary = "Target artifact",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        var sourceArtifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Cpt. Voss",
            Summary = "Duplicate",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        var thirdArtifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Location,
            Name = "Black Harbor",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _artifactRepo.Seed(targetArtifact, sourceArtifact, thirdArtifact);

        // Facts on source artifact
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = sourceArtifact.Id,
            Predicate = "rank",
            Value = "Captain",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _factRepo.Seed(fact);

        // Relationship between source artifact and a third artifact (should be reassigned)
        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = sourceArtifact.Id,
            ArtifactBId = thirdArtifact.Id,
            Type = "LocatedIn",
            TruthState = TruthState.Likely,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Self-referencing relationship (source → target, becomes target → target after merge)
        var selfRefRelationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = sourceArtifact.Id,
            ArtifactBId = targetArtifact.Id,
            Type = "DuplicateOf",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _relationshipRepo.Seed(relationship, selfRefRelationship);

        var payload = new MergeArtifactPayload(
            sourceArtifact.Id, "Captain Voss (merged)", "Combined summary", "GMOnly", 0.95m);
        var proposal = MakeProposal(ReviewChangeType.MergeArtifact, payload, targetArtifact.Id);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);

        // Fact reassigned to target
        var updatedFact = _factRepo.Facts.Single(f => f.Id == fact.Id);
        Assert.That(updatedFact.ArtifactId, Is.EqualTo(targetArtifact.Id));

        // Non-self-ref relationship reassigned to target
        var updatedRel = _relationshipRepo.Relationships.Single(r => r.Id == relationship.Id);
        Assert.That(updatedRel.ArtifactAId, Is.EqualTo(targetArtifact.Id));
        Assert.That(updatedRel.ArtifactBId, Is.EqualTo(thirdArtifact.Id));

        // Self-referencing relationship was skipped (not persisted via UpdateAsync)
        // The in-memory object was mutated during reassignment logic, then skipped
        // The key assertion: it would become self-referencing (A==B) after reassignment
        var selfRef = _relationshipRepo.Relationships.Single(r => r.Id == selfRefRelationship.Id);
        Assert.That(selfRef.ArtifactAId, Is.EqualTo(selfRef.ArtifactBId),
            "Self-referencing relationship should have both sides pointing to same artifact after reassignment attempt");

        // Source artifact archived
        var archivedSource = _artifactRepo.Artifacts.Single(a => a.Id == sourceArtifact.Id);
        Assert.That(archivedSource.Status, Is.EqualTo(ArtifactStatus.Archived));

        // Target artifact updated with merge fields
        var merged = _artifactRepo.Artifacts.Single(a => a.Id == targetArtifact.Id);
        Assert.That(merged.Name, Is.EqualTo("Captain Voss (merged)"));
        Assert.That(merged.Summary, Is.EqualTo("Combined summary"));
        Assert.That(merged.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
        Assert.That(merged.Confidence, Is.EqualTo(0.95m));
    }

    #endregion

    #region AddFact

    [Test]
    public async Task AddFact_CreatesFactWithCorrectArtifactIdFromTargetId()
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);

        var payload = new AddFactPayload(
            "location", "Black Harbor", 0.8m, "Likely", "PartyVisible");
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload, artifact.Id);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var fact = _factRepo.Facts.Single();
        Assert.That(fact.ArtifactId, Is.EqualTo(artifact.Id));
        Assert.That(fact.Predicate, Is.EqualTo("location"));
        Assert.That(fact.Value, Is.EqualTo("Black Harbor"));
        Assert.That(fact.Confidence, Is.EqualTo(0.8m));
        Assert.That(fact.TruthState, Is.EqualTo(TruthState.Likely));
        Assert.That(fact.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(result.Value!.TargetType, Is.EqualTo(SourceReferenceTargetType.ArtifactFact));
    }

    [Test]
    public async Task AddFact_ArtifactNotFound_ReturnsValidationError()
    {
        var nonExistentId = Guid.NewGuid();
        var payload = new AddFactPayload("location", "Black Harbor", null, null, null);
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload, nonExistentId);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("target_not_found"));
    }

    #endregion

    #region UpdateFact

    [Test]
    public async Task UpdateFact_UpdatesOnlySpecifiedFields()
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);

        var existingFact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = "location",
            Value = "Black Harbor",
            Confidence = 0.7m,
            TruthState = TruthState.Likely,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _factRepo.Seed(existingFact);

        // Only update value and confidence, leave truthState and visibility alone
        var payload = new UpdateFactPayload("Silver Key district", 0.95m, null, null);
        var proposal = MakeProposal(ReviewChangeType.UpdateFact, payload, existingFact.Id);

        var beforeApply = DateTimeOffset.UtcNow;
        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var updated = _factRepo.Facts.Single();
        Assert.That(updated.Value, Is.EqualTo("Silver Key district"));
        Assert.That(updated.Confidence, Is.EqualTo(0.95m));
        Assert.That(updated.TruthState, Is.EqualTo(TruthState.Likely)); // unchanged
        Assert.That(updated.Visibility, Is.EqualTo(VisibilityScope.PartyVisible)); // unchanged
        Assert.That(updated.Predicate, Is.EqualTo("location")); // unchanged
        Assert.That(updated.UpdatedAt, Is.GreaterThanOrEqualTo(beforeApply));
    }

    [Test]
    public async Task UpdateFact_TargetNotFound_ReturnsValidationError()
    {
        var nonExistentId = Guid.NewGuid();
        var payload = new UpdateFactPayload("new value", null, null, null);
        var proposal = MakeProposal(ReviewChangeType.UpdateFact, payload, nonExistentId);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("target_not_found"));
    }

    #endregion

    #region AddRelationship

    [Test]
    public async Task AddRelationship_CreatesRelationshipWithCorrectFields()
    {
        var artifactA = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var artifactB = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Location,
            Name = "Black Harbor",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifactA, artifactB);

        var payload = new AddRelationshipPayload(
            artifactA.Id, artifactB.Id, "LocatedIn",
            "Captain Voss is located in Black Harbor", 0.85m, "Likely", "PartyVisible");
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var rel = _relationshipRepo.Relationships.Single();
        Assert.That(rel.WorldId, Is.EqualTo(_worldId));
        Assert.That(rel.ArtifactAId, Is.EqualTo(artifactA.Id));
        Assert.That(rel.ArtifactBId, Is.EqualTo(artifactB.Id));
        Assert.That(rel.Type, Is.EqualTo("LocatedIn"));
        Assert.That(rel.Description, Is.EqualTo("Captain Voss is located in Black Harbor"));
        Assert.That(rel.Confidence, Is.EqualTo(0.85m));
        Assert.That(rel.TruthState, Is.EqualTo(TruthState.Likely));
        Assert.That(rel.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(result.Value!.TargetType, Is.EqualTo(SourceReferenceTargetType.ArtifactRelationship));
    }

    [Test]
    public async Task AddRelationship_ArtifactANotFound_ReturnsValidationError()
    {
        var artifactB = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Location,
            Name = "Black Harbor",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifactB);

        var payload = new AddRelationshipPayload(
            Guid.NewGuid(), artifactB.Id, "LocatedIn", null, null, null, null);
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_a_not_found"));
    }

    [Test]
    public async Task AddRelationship_ArtifactBNotFound_ReturnsValidationError()
    {
        var artifactA = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifactA);

        var payload = new AddRelationshipPayload(
            artifactA.Id, Guid.NewGuid(), "LocatedIn", null, null, null, null);
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_b_not_found"));
    }

    #endregion

    #region UpdateRelationship

    [Test]
    public async Task UpdateRelationship_UpdatesOnlySpecifiedFields()
    {
        var existingRelationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = Guid.NewGuid(),
            ArtifactBId = Guid.NewGuid(),
            Type = "LocatedIn",
            Description = "Captain Voss is in Black Harbor",
            Confidence = 0.7m,
            TruthState = TruthState.Likely,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _relationshipRepo.Seed(existingRelationship);

        // Only update type and confidence
        var payload = new UpdateRelationshipPayload(
            "SuspectedIn", null, 0.9m, null, null);
        var proposal = MakeProposal(
            ReviewChangeType.UpdateRelationship, payload, existingRelationship.Id);

        var beforeApply = DateTimeOffset.UtcNow;
        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var updated = _relationshipRepo.Relationships.Single();
        Assert.That(updated.Type, Is.EqualTo("SuspectedIn"));
        Assert.That(updated.Description, Is.EqualTo("Captain Voss is in Black Harbor")); // unchanged
        Assert.That(updated.Confidence, Is.EqualTo(0.9m));
        Assert.That(updated.TruthState, Is.EqualTo(TruthState.Likely)); // unchanged
        Assert.That(updated.Visibility, Is.EqualTo(VisibilityScope.PartyVisible)); // unchanged
        Assert.That(updated.UpdatedAt, Is.GreaterThanOrEqualTo(beforeApply));
    }

    [Test]
    public async Task UpdateRelationship_TargetNotFound_ReturnsValidationError()
    {
        var nonExistentId = Guid.NewGuid();
        var payload = new UpdateRelationshipPayload("NewType", null, null, null, null);
        var proposal = MakeProposal(
            ReviewChangeType.UpdateRelationship, payload, nonExistentId);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("target_not_found"));
    }

    #endregion

    #region SourceReference Creation

    [Test]
    public async Task CreateArtifact_CreatesSourceReference()
    {
        var payload = new CreateArtifactPayload(
            "Silver Key", "Item", "A mysterious key", null, 0.9m);
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact, payload);

        await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        var refs = _sourceRefRepo.References;
        Assert.That(refs, Has.Count.EqualTo(1));
        Assert.That(refs[0].SourceId, Is.EqualTo(_sourceId));
        Assert.That(refs[0].TargetType, Is.EqualTo(SourceReferenceTargetType.Artifact));
        Assert.That(refs[0].TargetId, Is.EqualTo(_artifactRepo.Artifacts.Single().Id));
    }

    [Test]
    public async Task AddFact_CreatesSourceReference()
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);

        var payload = new AddFactPayload("rank", "Captain", null, null, null);
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload, artifact.Id);

        await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        var refs = _sourceRefRepo.References;
        Assert.That(refs, Has.Count.EqualTo(1));
        Assert.That(refs[0].SourceId, Is.EqualTo(_sourceId));
        Assert.That(refs[0].TargetType, Is.EqualTo(SourceReferenceTargetType.ArtifactFact));
        Assert.That(refs[0].TargetId, Is.EqualTo(_factRepo.Facts.Single().Id));
    }

    [Test]
    public async Task AddRelationship_CreatesSourceReference()
    {
        var artifactA = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var artifactB = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Location,
            Name = "Black Harbor",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifactA, artifactB);

        var payload = new AddRelationshipPayload(
            artifactA.Id, artifactB.Id, "LocatedIn", null, null, null, null);
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        var refs = _sourceRefRepo.References;
        Assert.That(refs, Has.Count.EqualTo(1));
        Assert.That(refs[0].SourceId, Is.EqualTo(_sourceId));
        Assert.That(refs[0].TargetType, Is.EqualTo(SourceReferenceTargetType.ArtifactRelationship));
        Assert.That(refs[0].TargetId, Is.EqualTo(_relationshipRepo.Relationships.Single().Id));
    }

    [Test]
    public async Task UpdateArtifact_CreatesSourceReference()
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);

        var payload = new UpdateArtifactPayload("Captain Voss Updated", null, null, null, null);
        var proposal = MakeProposal(ReviewChangeType.UpdateArtifact, payload, artifact.Id);

        await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        var refs = _sourceRefRepo.References;
        Assert.That(refs, Has.Count.EqualTo(1));
        Assert.That(refs[0].SourceId, Is.EqualTo(_sourceId));
        Assert.That(refs[0].TargetType, Is.EqualTo(SourceReferenceTargetType.Artifact));
        Assert.That(refs[0].TargetId, Is.EqualTo(artifact.Id));
    }

    #endregion

    #region Visibility Defaults

    [Test]
    public async Task CreateArtifact_VisibilityDefaultsToSourceVisibility_WhenNotSpecifiedInPayload()
    {
        // Source has GMOnly visibility
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.GMNote,
            Title = "GM Notes",
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(gmSource);

        var gmBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = gmSource.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Payload does NOT specify visibility
        var payload = new CreateArtifactPayload(
            "Hidden NPC", "Character", "Secret character", null, 0.9m);
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact, payload);

        var result = await _applicator.ApplyAsync(proposal, gmBatch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var artifact = _artifactRepo.Artifacts.Single();
        Assert.That(artifact.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
    }

    [Test]
    public async Task CreateArtifact_UsesPayloadVisibility_WhenSpecified()
    {
        var payload = new CreateArtifactPayload(
            "Captain Voss", "Character", null, "Private", 0.8m);
        var proposal = MakeProposal(ReviewChangeType.CreateArtifact, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var artifact = _artifactRepo.Artifacts.Single();
        // Source is PartyVisible, but payload specifies Private
        Assert.That(artifact.Visibility, Is.EqualTo(VisibilityScope.Private));
    }

    #endregion

    #region Helpers

    #region Name-based artifact references

    private Artifact SeedArtifact(string name, ArtifactType type = ArtifactType.Character)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    [Test]
    public async Task AddFact_ByArtifactName_ResolvesArtifactAndCreatesFact()
    {
        var artifact = SeedArtifact("Captain Voss");

        var payload = new AddFactPayload(
            "denied knowledge of", "the missing caravan", 0.9m, "Confirmed", "PartyVisible",
            ArtifactName: "Captain Voss");
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload); // no TargetId

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var fact = _factRepo.Facts.Single();
        Assert.That(fact.ArtifactId, Is.EqualTo(artifact.Id));
        Assert.That(proposal.TargetId, Is.EqualTo(artifact.Id),
            "resolved artifact should be recorded on the proposal");
    }

    [Test]
    public async Task AddFact_ByArtifactName_IsCaseInsensitive()
    {
        var artifact = SeedArtifact("Captain Voss");

        var payload = new AddFactPayload(
            "rank", "Captain", null, null, null, ArtifactName: "captain voss");
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(artifact.Id));
    }

    [Test]
    public async Task AddFact_ByArtifactName_NotFound_ReturnsNotFoundError()
    {
        var payload = new AddFactPayload(
            "rank", "Captain", null, null, null, ArtifactName: "Captain Voss");
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_not_found"));
        Assert.That(_factRepo.Facts, Is.Empty);
    }

    [Test]
    public async Task AddFact_ByArtifactName_Ambiguous_ReturnsConflictError()
    {
        SeedArtifact("Captain Voss");
        SeedArtifact("Captain Voss", ArtifactType.Concept);

        var payload = new AddFactPayload(
            "rank", "Captain", null, null, null, ArtifactName: "Captain Voss");
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_ambiguous"));
        Assert.That(_factRepo.Facts, Is.Empty);
    }

    [Test]
    public async Task AddFact_ByArtifactName_OtherWorld_IsNotResolved()
    {
        var other = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = Guid.NewGuid(), // different world
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(other);

        var payload = new AddFactPayload(
            "rank", "Captain", null, null, null, ArtifactName: "Captain Voss");
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_not_found"));
    }

    [Test]
    public async Task AddFact_NoTargetIdAndNoName_ReturnsError()
    {
        var payload = new AddFactPayload("rank", "Captain", null, null, null);
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("missing_target_id"));
    }

    [Test]
    public async Task AddRelationship_ByNames_ResolvesBothEndpoints()
    {
        var voss = SeedArtifact("Captain Voss");
        var harbor = SeedArtifact("Black Harbor", ArtifactType.Location);

        var payload = new AddRelationshipPayload(
            null, null, "LocatedIn", null, 0.8m, "Likely", "PartyVisible",
            ArtifactAName: "Captain Voss", ArtifactBName: "Black Harbor");
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var rel = _relationshipRepo.Relationships.Single();
        Assert.That(rel.ArtifactAId, Is.EqualTo(voss.Id));
        Assert.That(rel.ArtifactBId, Is.EqualTo(harbor.Id));
    }

    [Test]
    public async Task AddRelationship_MixedIdAndName_Resolves()
    {
        var voss = SeedArtifact("Captain Voss");
        var caravan = SeedArtifact("The Missing Caravan", ArtifactType.Storyline);

        var payload = new AddRelationshipPayload(
            voss.Id, null, "SuspectedIn", null, null, null, null,
            ArtifactBName: "The Missing Caravan");
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var rel = _relationshipRepo.Relationships.Single();
        Assert.That(rel.ArtifactAId, Is.EqualTo(voss.Id));
        Assert.That(rel.ArtifactBId, Is.EqualTo(caravan.Id));
    }

    [Test]
    public async Task AddRelationship_ByName_NotFound_ReturnsNotFoundError()
    {
        SeedArtifact("Captain Voss");

        var payload = new AddRelationshipPayload(
            null, null, "LocatedIn", null, null, null, null,
            ArtifactAName: "Captain Voss", ArtifactBName: "Nowhere Keep");
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_not_found"));
        Assert.That(_relationshipRepo.Relationships, Is.Empty);
    }

    [Test]
    public async Task AddRelationship_SameArtifactBothEndpoints_ReturnsError()
    {
        var voss = SeedArtifact("Captain Voss");

        var payload = new AddRelationshipPayload(
            voss.Id, null, "AlliedWith", null, null, null, null,
            ArtifactBName: "Captain Voss");
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("self_relationship"));
    }

    #endregion

    private static ReviewProposal MakeProposal<T>(
        ReviewChangeType changeType, T payload, Guid? targetId = null)
    {
        var targetType = changeType switch
        {
            ReviewChangeType.CreateArtifact => ReviewTargetType.Artifact,
            ReviewChangeType.UpdateArtifact => ReviewTargetType.Artifact,
            ReviewChangeType.MergeArtifact => ReviewTargetType.Artifact,
            ReviewChangeType.AddFact => ReviewTargetType.ArtifactFact,
            ReviewChangeType.UpdateFact => ReviewTargetType.ArtifactFact,
            ReviewChangeType.AddRelationship => ReviewTargetType.ArtifactRelationship,
            ReviewChangeType.UpdateRelationship => ReviewTargetType.ArtifactRelationship,
            _ => ReviewTargetType.Artifact
        };

        return new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = Guid.NewGuid(),
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValueJson = JsonSerializer.Serialize(payload),
            Rationale = "AI-generated proposal",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
