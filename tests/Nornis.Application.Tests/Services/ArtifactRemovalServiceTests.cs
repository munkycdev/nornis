using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Per-artifact "remove from canon": deletes only what hangs off the target artifact — its
/// facts, the relationships touching it (either end), its map pins, and all of their
/// provenance — while every other artifact (including the far end of each relationship) and
/// its knowledge survives. Player-character links are cleared, not blocked. GM-only.
/// </summary>
[TestFixture]
[Category("Feature: artifact-removal")]
public class ArtifactRemovalServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemoryArtifactFactRepository _factRepository = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepository = null!;
    private InMemorySourceReferenceRepository _referenceRepository = null!;
    private InMemoryMapPlacemarkRepository _mapPlacemarkRepository = null!;
    private InMemoryCharacterRepository _characterRepository = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private ArtifactRemovalService _sut = null!;

    // Canonical scenario ids.
    private Guid _vossId;      // target of removal
    private Guid _harborId;    // related — must survive
    private Guid _lodgeId;     // related — must survive
    private Guid _vossFact1;
    private Guid _vossFact2;
    private Guid _harborFact;  // must survive
    private Guid _relLocatedIn;   // Voss (A) -> Harbor (B)
    private Guid _relOpposes;     // Lodge (A) -> Voss (B)
    private Guid _relForeign;     // Harbor (A) -> Lodge (B) — must survive
    private Guid _vossPin;
    private Guid _harborPin;    // must survive
    private Guid _characterId;

    [SetUp]
    public void SetUp()
    {
        _artifactRepository = new InMemoryArtifactRepository();
        _factRepository = new InMemoryArtifactFactRepository();
        _relationshipRepository = new InMemoryArtifactRelationshipRepository();
        _referenceRepository = new InMemorySourceReferenceRepository();
        _mapPlacemarkRepository = new InMemoryMapPlacemarkRepository();
        _characterRepository = new InMemoryCharacterRepository();
        _unitOfWork = new FakeUnitOfWork();

        _sut = new ArtifactRemovalService(
            _artifactRepository,
            _factRepository,
            _relationshipRepository,
            _referenceRepository,
            _mapPlacemarkRepository,
            _characterRepository,
            _unitOfWork,
            NullLogger<ArtifactRemovalService>.Instance);
    }

    private void SeedScenario()
    {
        _vossId = SeedArtifact("Captain Voss");
        _harborId = SeedArtifact("Black Harbor");
        _lodgeId = SeedArtifact("Red Lodge");

        _vossFact1 = SeedFact(_vossId, "location", "at sea");
        _vossFact2 = SeedFact(_vossId, "title", "captain");
        _harborFact = SeedFact(_harborId, "region", "north");

        _relLocatedIn = SeedRelationship(_vossId, _harborId, "LocatedIn");
        _relOpposes = SeedRelationship(_lodgeId, _vossId, "Opposes");
        _relForeign = SeedRelationship(_harborId, _lodgeId, "Near");

        _vossPin = SeedPin(_vossId);
        _harborPin = SeedPin(_harborId);

        // Provenance: refs to Voss and its facts/relationships (all deleted), plus a Harbor ref (survives).
        _referenceRepository.Seed(
            Reference(SourceReferenceTargetType.Artifact, _vossId),
            Reference(SourceReferenceTargetType.ArtifactFact, _vossFact1),
            Reference(SourceReferenceTargetType.ArtifactFact, _vossFact2),
            Reference(SourceReferenceTargetType.ArtifactRelationship, _relLocatedIn),
            Reference(SourceReferenceTargetType.ArtifactRelationship, _relOpposes),
            Reference(SourceReferenceTargetType.Artifact, _harborId));

        _characterId = SeedCharacterLinkedTo(_vossId);
    }

    // ------------------------------------------------------------------ helpers --

    private Guid SeedArtifact(string name)
    {
        var id = Guid.NewGuid();
        _artifactRepository.Seed(new Artifact
        {
            Id = id,
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = name,
            Status = ArtifactStatus.Active,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private Guid SeedFact(Guid artifactId, string predicate, string value)
    {
        var id = Guid.NewGuid();
        _factRepository.Seed(new ArtifactFact
        {
            Id = id,
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private Guid SeedRelationship(Guid aId, Guid bId, string type)
    {
        var id = Guid.NewGuid();
        _relationshipRepository.Seed(new ArtifactRelationship
        {
            Id = id,
            WorldId = WorldId,
            ArtifactAId = aId,
            ArtifactBId = bId,
            Type = type,
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private Guid SeedPin(Guid artifactId)
    {
        var id = Guid.NewGuid();
        _mapPlacemarkRepository.Seed(new MapPlacemark
        {
            Id = id,
            SourceAttachmentId = Guid.NewGuid(),
            ArtifactId = artifactId
        });
        return id;
    }

    private SourceReference Reference(SourceReferenceTargetType targetType, Guid targetId) => new()
    {
        Id = Guid.NewGuid(),
        SourceId = Guid.NewGuid(),
        TargetType = targetType,
        TargetId = targetId,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private Guid SeedCharacterLinkedTo(Guid artifactId)
    {
        var id = Guid.NewGuid();
        _characterRepository.Seed(new Character
        {
            Id = id,
            WorldId = WorldId,
            WorldMemberId = Guid.NewGuid(),
            Name = "Tavrin",
            ArtifactId = artifactId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private RemoveArtifactCommand RemoveCommand(Guid artifactId, WorldRole role = WorldRole.GM) =>
        new(WorldId, artifactId, Guid.NewGuid(), role);

    // ------------------------------------------------------------------ Preview --

    [Test]
    public async Task PreviewAsync_ReportsCountsWithoutDeletingAnything()
    {
        SeedScenario();

        var result = await _sut.PreviewAsync(WorldId, _vossId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var preview = result.Value!;
        Assert.That(preview.ArtifactName, Is.EqualTo("Captain Voss"));
        Assert.That(preview.FactCount, Is.EqualTo(2));
        Assert.That(preview.Relationships, Has.Count.EqualTo(2));
        Assert.That(preview.Relationships, Has.Some.Contains("Black Harbor"));
        Assert.That(preview.Relationships, Has.Some.Contains("Red Lodge"));
        Assert.That(preview.MapPinCount, Is.EqualTo(1));
        Assert.That(preview.CharacterLinksToClear, Is.EqualTo(1));

        // Nothing deleted.
        Assert.That(_artifactRepository.Artifacts, Has.Count.EqualTo(3));
        Assert.That(_factRepository.Facts, Has.Count.EqualTo(3));
        Assert.That(_relationshipRepository.Relationships, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task PreviewAsync_AsNonGm_Returns403()
    {
        SeedScenario();

        var result = await _sut.PreviewAsync(WorldId, _vossId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    // ------------------------------------------------------------------- Remove --

    [Test]
    public async Task RemoveAsync_DeletesTargetAndItsAttachedKnowledgeOnly()
    {
        SeedScenario();

        var result = await _sut.RemoveAsync(RemoveCommand(_vossId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);

        // The target is gone; the two related artifacts survive.
        Assert.That(_artifactRepository.Artifacts.Select(a => a.Id), Is.EquivalentTo(new[] { _harborId, _lodgeId }));

        // Only Voss's facts go; Harbor's fact survives.
        Assert.That(_factRepository.Facts.Select(f => f.Id), Is.EquivalentTo(new[] { _harborFact }));

        // Both relationships touching Voss go; the Harbor–Lodge relationship survives.
        Assert.That(_relationshipRepository.Relationships.Select(r => r.Id), Is.EquivalentTo(new[] { _relForeign }));

        // Voss's pin goes; Harbor's pin survives.
        Assert.That(_mapPlacemarkRepository.Placemarks.Select(p => p.Id), Is.EquivalentTo(new[] { _harborPin }));

        // Provenance for the deleted entities is gone; the Harbor ref survives.
        Assert.That(_referenceRepository.References, Has.Count.EqualTo(1));
        Assert.That(_referenceRepository.References.Single().TargetId, Is.EqualTo(_harborId));

        // Committed.
        Assert.That(_unitOfWork.Transactions.Single().Committed, Is.True);
    }

    [Test]
    public async Task RemoveAsync_ClearsPlayerCharacterLink()
    {
        SeedScenario();

        await _sut.RemoveAsync(RemoveCommand(_vossId), CancellationToken.None);

        var character = _characterRepository.Characters.Single(c => c.Id == _characterId);
        Assert.That(character.ArtifactId, Is.Null);
    }

    [Test]
    public async Task RemoveAsync_WithNoAttachedKnowledge_DeletesJustTheArtifact()
    {
        var loneId = SeedArtifact("Lone Concept");

        var result = await _sut.RemoveAsync(RemoveCommand(loneId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_artifactRepository.Artifacts, Is.Empty);
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task RemoveAsync_AsNonGm_Returns403AndDeletesNothing(WorldRole role)
    {
        SeedScenario();

        var result = await _sut.RemoveAsync(RemoveCommand(_vossId, role), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_artifactRepository.Artifacts, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task RemoveAsync_UnknownArtifact_Returns404()
    {
        var result = await _sut.RemoveAsync(RemoveCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task RemoveAsync_ArtifactInAnotherWorld_Returns404()
    {
        var foreignId = Guid.NewGuid();
        _artifactRepository.Seed(new Artifact
        {
            Id = foreignId,
            WorldId = Guid.NewGuid(),
            Type = ArtifactType.Character,
            Name = "Elsewhere",
            Status = ArtifactStatus.Active,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.RemoveAsync(RemoveCommand(foreignId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task RemoveAsync_CommitFailure_RollsBackAndReturns500()
    {
        SeedScenario();
        _unitOfWork.ConfigureCommitFailure();

        var result = await _sut.RemoveAsync(RemoveCommand(_vossId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(500));
        Assert.That(_unitOfWork.Transactions.Single().RolledBack, Is.True);
    }
}
