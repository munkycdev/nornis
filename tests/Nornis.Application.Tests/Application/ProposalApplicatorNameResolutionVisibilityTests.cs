using System.Text.Json;
using Nornis.Application.Application;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Application;

/// <summary>
/// Name-referenced payloads (AddFact, AddRelationship, AddPlacemark) resolve through the
/// accepting reviewer's VisibilityFilter.
///
/// This is Player-reachable, which is what makes it matter: ReviewService lets a Player
/// review proposals on sources they created, and the proposal payload is Player-editable
/// via POST proposals/{id}/edit. An unfiltered name lookup here would let a Player both
/// bind their own facts onto artifacts they cannot see and probe the world's artifact
/// table for names by watching which error came back.
/// </summary>
[TestFixture]
public class ProposalApplicatorNameResolutionVisibilityTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private ProposalApplicator _applicator = null!;

    private Guid _worldId;
    private Guid _sourceId;
    private Guid _playerId;
    private Guid _otherPlayerId;
    private ReviewBatch _batch = null!;

    /// <summary>What the Player accepting a proposal on their own source may see.</summary>
    private VisibilityFilter PlayerFilter => VisibilityFilter.ForRole(WorldRole.Player, _playerId);

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
            _sourceRepo,
            new InMemorySourceAttachmentRepository(),
            new InMemoryMapPlacemarkRepository());

        _worldId = Guid.NewGuid();
        _sourceId = Guid.NewGuid();
        _playerId = Guid.NewGuid();
        _otherPlayerId = Guid.NewGuid();

        // The Player's own source — this is what gets them past CheckReviewAuthorization.
        _sourceRepo.Seed(new Source
        {
            Id = _sourceId,
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 4: what I overheard",
            Body = "Notes from the player.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _playerId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });

        _batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = _sourceId,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
    }

    #region Cross-visibility write bind

    [Test]
    public async Task AddFact_PlayerNamingGMOnlyArtifact_DoesNotResolve()
    {
        var secret = SeedArtifact("The Lich's True Name", VisibilityScope.GMOnly);

        var result = await ApplyAddFactByName("The Lich's True Name", PlayerFilter);

        Assert.That(result.IsSuccess, Is.False,
            "a Player must not bind a fact onto a GM-only artifact");
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_not_found"));
        Assert.That(_factRepo.Facts, Is.Empty);
        Assert.That(_factRepo.Facts.Any(f => f.ArtifactId == secret.Id), Is.False);
    }

    [Test]
    public async Task AddFact_PlayerNamingAnotherUsersPrivateArtifact_DoesNotResolve()
    {
        var theirs = SeedArtifact("Backstory Notes", VisibilityScope.Private, _otherPlayerId);

        var result = await ApplyAddFactByName("Backstory Notes", PlayerFilter);

        Assert.That(result.IsSuccess, Is.False,
            "a Player must not bind a fact onto another user's Private artifact");
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_not_found"));
        Assert.That(_factRepo.Facts, Is.Empty);
        Assert.That(_factRepo.Facts.Any(f => f.ArtifactId == theirs.Id), Is.False);
    }

    [Test]
    public async Task AddFact_NameMatchingOnlyAnArchivedArtifact_DoesNotResolve()
    {
        // Unrestricted filter, so this isolates the Archived exclusion from visibility:
        // even a GM must not bind new facts onto a merge leftover.
        SeedArtifact("Captain Voss", VisibilityScope.PartyVisible, status: ArtifactStatus.Archived);

        var result = await ApplyAddFactByName("Captain Voss", VisibilityFilter.All);

        Assert.That(result.IsSuccess, Is.False,
            "an archived artifact is a merge leftover — new facts must not attach to it");
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_not_found"));
        Assert.That(_factRepo.Facts, Is.Empty);
    }

    [Test]
    public async Task AddRelationship_PlayerNamingGMOnlyEndpoint_DoesNotResolve()
    {
        SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedArtifact("The Lich's True Name", VisibilityScope.GMOnly);

        var payload = new AddRelationshipPayload(
            null, null, "AlliedWith", null, null, null, null,
            ArtifactAName: "Captain Voss", ArtifactBName: "The Lich's True Name");
        var proposal = MakeProposal(ReviewChangeType.AddRelationship, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, PlayerFilter, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False,
            "a relationship endpoint is a name reference too — same gate applies");
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_not_found"));
        Assert.That(_relationshipRepo.Relationships, Is.Empty);
    }

    [Test]
    public async Task AddPlacemark_PlayerNamingGMOnlyArtifact_DoesNotResolve()
    {
        SeedArtifact("Hidden Vault", VisibilityScope.GMOnly);

        var payload = new AddPlacemarkPayload(
            null, "Hidden Vault", Guid.NewGuid(), 0.5m, 0.5m, "Vault", null);
        var proposal = MakeProposal(ReviewChangeType.AddPlacemark, payload);

        var result = await _applicator.ApplyAsync(proposal, _batch, PlayerFilter, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False,
            "a Player must not drop a map pin onto a GM-only location");
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_not_found"));
    }

    #endregion

    #region Existence oracle

    [Test]
    public async Task AddFact_PlayerNamingGMOnlyArtifact_IsIndistinguishableFromANameThatDoesNotExist()
    {
        // Nothing seeded yet: this name genuinely does not exist in the world.
        var absent = await ApplyAddFactByName("The Lich's True Name", PlayerFilter);

        // Now it exists — but it is GM-only, so the Player still may not see it.
        SeedArtifact("The Lich's True Name", VisibilityScope.GMOnly);
        var hidden = await ApplyAddFactByName("The Lich's True Name", PlayerFilter);

        Assert.That(absent.IsSuccess, Is.False);
        Assert.That(hidden.IsSuccess, Is.False);
        Assert.That(hidden.Error!.StatusCode, Is.EqualTo(absent.Error!.StatusCode));
        Assert.That(hidden.Error!.Code, Is.EqualTo(absent.Error!.Code));
        Assert.That(hidden.Error!.Message, Is.EqualTo(absent.Error!.Message),
            "a hidden artifact and a nonexistent one must be externally identical, "
            + "or accept becomes a name-probe over the world's artifact table");
    }

    [Test]
    public async Task AddFact_AmbiguityIsComputedOverTheVisibleSetOnly()
    {
        // Two artifacts share a name; only one is visible to the Player. If the filter were
        // applied after counting, the Player would get 409 "Multiple artifacts are named X"
        // — which itself discloses that a second, hidden one exists.
        var visible = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedArtifact("Captain Voss", VisibilityScope.GMOnly);

        var result = await ApplyAddFactByName("Captain Voss", PlayerFilter);

        Assert.That(result.IsSuccess, Is.True,
            "the hidden duplicate must not turn a clean resolution into an ambiguity report");
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(visible.Id));
    }

    #endregion

    #region Positive controls — the filter narrows, it does not break resolution

    [Test]
    public async Task AddFact_PlayerNamingPartyVisibleArtifact_StillResolves()
    {
        var voss = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);

        var result = await ApplyAddFactByName("Captain Voss", PlayerFilter);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(voss.Id));
    }

    [Test]
    public async Task AddFact_PlayerNamingTheirOwnPrivateArtifact_StillResolves()
    {
        var mine = SeedArtifact("My Character's Secret", VisibilityScope.Private, _playerId);

        var result = await ApplyAddFactByName("My Character's Secret", PlayerFilter);

        Assert.That(result.IsSuccess, Is.True,
            "Private is gated by ownership, not blanket-denied");
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(mine.Id));
    }

    [Test]
    public async Task AddFact_GMNamingGMOnlyArtifact_StillResolves()
    {
        var secret = SeedArtifact("The Lich's True Name", VisibilityScope.GMOnly);

        var result = await ApplyAddFactByName(
            "The Lich's True Name", VisibilityFilter.ForRole(WorldRole.GM, Guid.NewGuid()));

        Assert.That(result.IsSuccess, Is.True, "a GM sees everything");
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(secret.Id));
    }

    #endregion

    #region Helpers

    private async Task<Nornis.Application.Errors.AppResult<ApplyResult>> ApplyAddFactByName(
        string artifactName, VisibilityFilter actingFilter)
    {
        var payload = new AddFactPayload(
            "was seen at", "the docks", 0.8m, "Likely", "PartyVisible",
            ArtifactName: artifactName);
        var proposal = MakeProposal(ReviewChangeType.AddFact, payload);

        return await _applicator.ApplyAsync(proposal, _batch, actingFilter, CancellationToken.None);
    }

    private Artifact SeedArtifact(
        string name,
        VisibilityScope visibility,
        Guid? createdByUserId = null,
        ArtifactStatus status = ArtifactStatus.Active)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = name,
            Visibility = visibility,
            Status = status,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private static ReviewProposal MakeProposal<T>(ReviewChangeType changeType, T payload)
    {
        var targetType = changeType switch
        {
            ReviewChangeType.AddFact => ReviewTargetType.ArtifactFact,
            ReviewChangeType.AddRelationship => ReviewTargetType.ArtifactRelationship,
            _ => ReviewTargetType.Artifact
        };

        return new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = Guid.NewGuid(),
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = null,
            ProposedValueJson = JsonSerializer.Serialize(payload),
            Rationale = "AI-generated proposal",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
