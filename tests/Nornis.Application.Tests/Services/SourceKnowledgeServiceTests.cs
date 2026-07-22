using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Resolving a source's provenance rows into reader-visible extracted knowledge:
/// grouped by kind, deduplicated, visibility-filtered, and tolerant of provenance
/// rows whose target has since been removed from canon.
/// </summary>
[TestFixture]
public class SourceKnowledgeServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();
    private static readonly Guid PlayerId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private InMemorySourceReferenceRepository _referenceRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemoryArtifactFactRepository _factRepository = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepository = null!;
    private SourceKnowledgeService _sut = null!;

    private Source _source = null!;
    private Guid _vossId;
    private Guid _harborId;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _referenceRepository = new InMemorySourceReferenceRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _factRepository = new InMemoryArtifactFactRepository();
        _relationshipRepository = new InMemoryArtifactRelationshipRepository();

        _sut = new SourceKnowledgeService(
            _sourceRepository, _referenceRepository, _artifactRepository,
            _factRepository, _relationshipRepository);

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 1",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmId
        };
        _sourceRepository.Seed(_source);

        _vossId = SeedArtifact("Captain Voss");
        _harborId = SeedArtifact("Black Harbor");
    }

    private Guid SeedArtifact(string name, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var id = Guid.NewGuid();
        _artifactRepository.Seed(new Artifact
        {
            Id = id,
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = name,
            Status = ArtifactStatus.Active,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private Guid SeedFact(Guid artifactId, string predicate, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var id = Guid.NewGuid();
        _factRepository.Seed(new ArtifactFact
        {
            Id = id,
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = "some value",
            TruthState = TruthState.Confirmed,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private void SeedReference(SourceReferenceTargetType targetType, Guid targetId, string? quote = null) =>
        _referenceRepository.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = _source.Id,
            TargetType = targetType,
            TargetId = targetId,
            Quote = quote,
            CreatedAt = DateTimeOffset.UtcNow
        });

    [Test]
    public async Task GetForSource_GroupsKnowledgeByKind_WithQuotes()
    {
        var factId = SeedFact(_vossId, "title");
        var relId = Guid.NewGuid();
        _relationshipRepository.Seed(new ArtifactRelationship
        {
            Id = relId,
            WorldId = WorldId,
            ArtifactAId = _vossId,
            ArtifactBId = _harborId,
            Type = "LocatedIn",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        SeedReference(SourceReferenceTargetType.Artifact, _vossId, "We questioned Captain Voss");
        SeedReference(SourceReferenceTargetType.ArtifactFact, factId, "he is a captain");
        SeedReference(SourceReferenceTargetType.ArtifactRelationship, relId);

        var result = await _sut.GetForSourceAsync(WorldId, _source.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var knowledge = result.Value!;
        Assert.That(knowledge.Artifacts, Has.Count.EqualTo(1));
        Assert.That(knowledge.Artifacts[0].Name, Is.EqualTo("Captain Voss"));
        Assert.That(knowledge.Artifacts[0].Quote, Is.EqualTo("We questioned Captain Voss"));
        Assert.That(knowledge.Facts, Has.Count.EqualTo(1));
        Assert.That(knowledge.Facts[0].ArtifactName, Is.EqualTo("Captain Voss"));
        Assert.That(knowledge.Facts[0].Predicate, Is.EqualTo("title"));
        Assert.That(knowledge.Relationships, Has.Count.EqualTo(1));
        Assert.That(knowledge.Relationships[0].ArtifactAName, Is.EqualTo("Captain Voss"));
        Assert.That(knowledge.Relationships[0].ArtifactBName, Is.EqualTo("Black Harbor"));
    }

    [Test]
    public async Task GetForSource_PlayerDoesNotSeeGmOnlyFacts()
    {
        var partyFact = SeedFact(_vossId, "title");
        var gmFact = SeedFact(_vossId, "secret", VisibilityScope.GMOnly);
        SeedReference(SourceReferenceTargetType.ArtifactFact, partyFact);
        SeedReference(SourceReferenceTargetType.ArtifactFact, gmFact);

        var result = await _sut.GetForSourceAsync(WorldId, _source.Id, PlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Facts.Select(f => f.Predicate), Is.EqualTo(new[] { "title" }));
    }

    [Test]
    public async Task GetForSource_SkipsDanglingProvenanceRows()
    {
        SeedReference(SourceReferenceTargetType.ArtifactFact, Guid.NewGuid());
        SeedReference(SourceReferenceTargetType.ArtifactRelationship, Guid.NewGuid());
        SeedReference(SourceReferenceTargetType.Artifact, _vossId);

        var result = await _sut.GetForSourceAsync(WorldId, _source.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Artifacts, Has.Count.EqualTo(1));
        Assert.That(result.Value.Facts, Is.Empty);
        Assert.That(result.Value.Relationships, Is.Empty);
    }

    [Test]
    public async Task GetForSource_DeduplicatesRepeatedTargets()
    {
        SeedReference(SourceReferenceTargetType.Artifact, _vossId, "first mention");
        SeedReference(SourceReferenceTargetType.Artifact, _vossId, "second mention");

        var result = await _sut.GetForSourceAsync(WorldId, _source.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Artifacts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetForSource_GmOnlySource_HiddenFromPlayers()
    {
        _source.Visibility = VisibilityScope.GMOnly;

        var result = await _sut.GetForSourceAsync(WorldId, _source.Id, PlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task GetForSource_WrongWorld_Returns404()
    {
        var result = await _sut.GetForSourceAsync(Guid.NewGuid(), _source.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }
}
