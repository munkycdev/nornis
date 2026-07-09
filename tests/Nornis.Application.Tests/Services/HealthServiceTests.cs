using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class HealthServiceTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private HealthService _service = null!;

    private Guid _worldId;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _service = new HealthService(_artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo);
        _worldId = Guid.NewGuid();
    }

    [Test]
    public async Task GetHealth_EmptyWorld_ReportsNoData()
    {
        var result = await _service.GetHealthAsync(_worldId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.HasData, Is.False);
        Assert.That(result.Value.OverallScore, Is.EqualTo(0));
        Assert.That(result.Value.Label, Is.EqualTo("Not enough data yet"));
    }

    [Test]
    public async Task GetHealth_FullyDevelopedGroundedRecent_ScoresPerfect()
    {
        var voss = MakeArtifact("Captain Voss", summary: "Harbourmaster.", updatedAt: DateTimeOffset.UtcNow);
        _artifactRepo.Seed(voss);
        var fact = MakeFact(voss.Id, TruthState.Confirmed);
        _factRepo.Seed(fact);
        await _sourceRefRepo.CreateAsync(MakeSourceRef(fact.Id));

        var result = await _service.GetHealthAsync(_worldId, CancellationToken.None);

        var h = result.Value!;
        Assert.That(h.HasData, Is.True);
        Assert.That(h.Consistency, Is.EqualTo(100));
        Assert.That(h.Completeness, Is.EqualTo(100));
        Assert.That(h.Groundedness, Is.EqualTo(100));
        Assert.That(h.Recency, Is.EqualTo(100));
        Assert.That(h.OverallScore, Is.EqualTo(100));
        Assert.That(h.Label, Is.EqualTo("Strong"));
    }

    [Test]
    public async Task GetHealth_Consistency_DropsWithDisputedStatements()
    {
        var voss = MakeArtifact("Captain Voss", summary: "Harbourmaster.");
        _artifactRepo.Seed(voss);
        _factRepo.Seed(
            MakeFact(voss.Id, TruthState.Confirmed),
            MakeFact(voss.Id, TruthState.Disputed));

        var result = await _service.GetHealthAsync(_worldId, CancellationToken.None);

        // 1 of 2 statements is a contradiction -> 50%.
        Assert.That(result.Value!.Consistency, Is.EqualTo(50));
    }

    [Test]
    public async Task GetHealth_Groundedness_ReflectsSourceReferences()
    {
        var voss = MakeArtifact("Captain Voss", summary: "Harbourmaster.");
        _artifactRepo.Seed(voss);
        var sourced = MakeFact(voss.Id, TruthState.Confirmed);
        var unsourced = MakeFact(voss.Id, TruthState.Confirmed);
        _factRepo.Seed(sourced, unsourced);
        await _sourceRefRepo.CreateAsync(MakeSourceRef(sourced.Id));

        var result = await _service.GetHealthAsync(_worldId, CancellationToken.None);

        // 1 of 2 statements is sourced -> 50%.
        Assert.That(result.Value!.Groundedness, Is.EqualTo(50));
    }

    [Test]
    public async Task GetHealth_Completeness_CountsOnlyDevelopedArtifacts()
    {
        var developed = MakeArtifact("Captain Voss", summary: "Harbourmaster.");
        var stub = MakeArtifact("Nameless Sailor", summary: null);
        _artifactRepo.Seed(developed, stub);
        _factRepo.Seed(MakeFact(developed.Id, TruthState.Confirmed));

        var result = await _service.GetHealthAsync(_worldId, CancellationToken.None);

        // 1 of 2 artifacts is developed (summary + a fact) -> 50%.
        Assert.That(result.Value!.Completeness, Is.EqualTo(50));
    }

    [Test]
    public async Task GetHealth_Recency_ReflectsRecentlyUpdatedArtifacts()
    {
        _artifactRepo.Seed(
            MakeArtifact("Fresh", summary: "x", updatedAt: DateTimeOffset.UtcNow),
            MakeArtifact("Stale", summary: "x", updatedAt: DateTimeOffset.UtcNow.AddDays(-60)));

        var result = await _service.GetHealthAsync(_worldId, CancellationToken.None);

        // 1 of 2 artifacts updated within the 30-day window -> 50%.
        Assert.That(result.Value!.Recency, Is.EqualTo(50));
    }

    private Artifact MakeArtifact(string name, string? summary = null, DateTimeOffset? updatedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = name,
            Summary = summary,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now,
        };
    }

    private static ArtifactFact MakeFact(Guid artifactId, TruthState truthState)
    {
        var now = DateTimeOffset.UtcNow;
        return new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = "predicate",
            Value = "value",
            TruthState = truthState,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static SourceReference MakeSourceRef(Guid targetId) => new()
    {
        Id = Guid.NewGuid(),
        SourceId = Guid.NewGuid(),
        TargetType = SourceReferenceTargetType.ArtifactFact,
        TargetId = targetId,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
