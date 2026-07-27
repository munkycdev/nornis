using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The <c>kind</c> and per-kind limit parameters on canon.
///
/// These exist because the invariants are not self-evident from the code: a cap must run AFTER
/// the visibility and truth-state filters (otherwise it consumes slots on entries the filters
/// should have removed, silently dropping visible ones), each kind must be capped before the two
/// are merged (otherwise a fact-heavy world returns no relationships), and narrowing by kind must
/// not change the content of the kind you asked for.
/// </summary>
[TestFixture]
public class CanonKindAndLimitTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private CanonService _service = null!;

    private Guid _worldId;
    private Guid _gmId;
    private Guid _playerId;
    private Artifact _voss = null!;
    private Artifact _harbor = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _service = new CanonService(_artifactRepo, _factRepo, _relationshipRepo);

        _worldId = Guid.NewGuid();
        _gmId = Guid.NewGuid();
        _playerId = Guid.NewGuid();

        _voss = MakeArtifact("Captain Voss");
        _harbor = MakeArtifact("Black Harbor");
        _artifactRepo.Seed(_voss, _harbor);
    }

    private Artifact MakeArtifact(string name) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = _worldId,
        Type = ArtifactType.Character,
        Name = name,
        Visibility = VisibilityScope.PartyVisible,
        Status = ArtifactStatus.Active,
        CreatedByUserId = _gmId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private ArtifactFact SeedFact(
        string predicate, int ageMinutes,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        TruthState truthState = TruthState.Confirmed)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = _voss.Id,
            Predicate = predicate,
            Value = "value",
            Visibility = visibility,
            TruthState = truthState,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes),
        };
        _factRepo.Seed(fact);
        return fact;
    }

    private void SeedRelationship(string type, int ageMinutes)
    {
        _relationshipRepo.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = _voss.Id,
            ArtifactBId = _harbor.Id,
            Type = type,
            Visibility = VisibilityScope.PartyVisible,
            TruthState = TruthState.Confirmed,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes),
        });
    }

    private CanonQuery Query(
        WorldRole role = WorldRole.GM,
        CanonEntryKind? kind = null,
        int? limit = null,
        int? factLimit = null,
        int? relationshipLimit = null) =>
        new(_worldId, role == WorldRole.GM ? _gmId : _playerId, role,
            Kind: kind, Limit: limit, FactLimit: factLimit, RelationshipLimit: relationshipLimit);

    // ------------------------------------------------------------------ kind

    [Test]
    public async Task KindFact_ReturnsOnlyFacts()
    {
        SeedFact("denied", 5);
        SeedRelationship("LocatedIn", 1);

        var result = await _service.GetCanonAsync(Query(kind: CanonEntryKind.Fact), CancellationToken.None);

        Assert.That(result.Value!.Select(e => e.Kind), Is.All.EqualTo(CanonEntryKind.Fact));
        Assert.That(result.Value!, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task KindRelationship_ReturnsOnlyRelationships()
    {
        SeedFact("denied", 5);
        SeedRelationship("LocatedIn", 1);

        var result = await _service.GetCanonAsync(Query(kind: CanonEntryKind.Relationship), CancellationToken.None);

        Assert.That(result.Value!.Select(e => e.Kind), Is.All.EqualTo(CanonEntryKind.Relationship));
        Assert.That(result.Value!, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task NarrowingByKind_DoesNotChangeTheContentOfThatKind()
    {
        // Guards the load-skipping optimisation: asking for facts alone must return exactly the
        // facts an unfiltered call would have returned.
        SeedFact("first", 10);
        SeedFact("second", 5);
        SeedRelationship("LocatedIn", 1);

        var all = await _service.GetCanonAsync(Query(), CancellationToken.None);
        var factsOnly = await _service.GetCanonAsync(Query(kind: CanonEntryKind.Fact), CancellationToken.None);

        Assert.That(
            factsOnly.Value!.Select(e => e.Id),
            Is.EqualTo(all.Value!.Where(e => e.Kind == CanonEntryKind.Fact).Select(e => e.Id)));
    }

    [Test]
    public async Task NoKind_ReturnsBoth()
    {
        SeedFact("denied", 5);
        SeedRelationship("LocatedIn", 1);

        var result = await _service.GetCanonAsync(Query(), CancellationToken.None);

        Assert.That(result.Value!.Select(e => e.Kind),
            Is.EquivalentTo(new[] { CanonEntryKind.Fact, CanonEntryKind.Relationship }));
    }

    // ------------------------------------------------------------------ limits

    [Test]
    public async Task PerKindLimits_KeepBothKindsRepresented()
    {
        // The reason per-kind limits exist: a single overall cap over a fact-heavy world would
        // return facts only, and the dashboard's relationships card would sit permanently empty.
        for (var i = 0; i < 20; i++)
        {
            SeedFact($"fact-{i}", i);
        }
        SeedRelationship("LocatedIn", 30);

        var result = await _service.GetCanonAsync(
            Query(factLimit: 3, relationshipLimit: 3), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Count(e => e.Kind == CanonEntryKind.Fact), Is.EqualTo(3));
            Assert.That(result.Value!.Count(e => e.Kind == CanonEntryKind.Relationship), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OverallLimit_AloneStarvesTheOlderKind()
    {
        // Documents precisely why the per-kind limits were added, so nobody "simplifies" them away.
        for (var i = 0; i < 20; i++)
        {
            SeedFact($"fact-{i}", i);
        }
        SeedRelationship("LocatedIn", 30);

        var result = await _service.GetCanonAsync(Query(limit: 3), CancellationToken.None);

        Assert.That(result.Value!.Count(e => e.Kind == CanonEntryKind.Relationship), Is.Zero);
    }

    [Test]
    public async Task PerKindLimit_TakesTheNewest()
    {
        SeedFact("oldest", 30);
        SeedFact("newest", 1);
        SeedFact("middle", 15);

        var result = await _service.GetCanonAsync(Query(factLimit: 2), CancellationToken.None);

        Assert.That(result.Value!.Select(e => e.Label), Is.EqualTo(new[] { "newest", "middle" }));
    }

    // ------------------------------------------------------------------ cap vs filters

    [Test]
    public async Task CapRunsAfterTheTruthStateFilter_SoHiddenEntriesNeverConsumeSlots()
    {
        // The invariant the code comments assert. Hidden entries are newest, so a cap applied
        // before the filter would spend both slots on them and return nothing to a Player.
        SeedFact("hidden-1", 1, truthState: TruthState.Hidden);
        SeedFact("hidden-2", 2, truthState: TruthState.Hidden);
        SeedFact("visible-1", 10);
        SeedFact("visible-2", 11);

        var asPlayer = await _service.GetCanonAsync(
            Query(WorldRole.Player, factLimit: 2), CancellationToken.None);

        Assert.That(asPlayer.Value!.Select(e => e.Label),
            Is.EqualTo(new[] { "visible-1", "visible-2" }),
            "a Player must get their two newest VISIBLE facts, not two empty slots");
    }

    [Test]
    public async Task CapRunsAfterTheVisibilityFilter()
    {
        SeedFact("gm-only-1", 1, visibility: VisibilityScope.GMOnly);
        SeedFact("gm-only-2", 2, visibility: VisibilityScope.GMOnly);
        SeedFact("party", 10);

        var asPlayer = await _service.GetCanonAsync(
            Query(WorldRole.Player, factLimit: 2), CancellationToken.None);

        Assert.That(asPlayer.Value!.Select(e => e.Label), Is.EqualTo(new[] { "party" }));
    }

    [Test]
    public async Task NullLimits_ReturnEverything()
    {
        for (var i = 0; i < 5; i++)
        {
            SeedFact($"fact-{i}", i);
        }

        var result = await _service.GetCanonAsync(Query(), CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(5));
    }
}
