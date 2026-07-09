using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class CanonServiceTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private CanonService _service = null!;

    // World: "Black Harbor Investigation"
    private Guid _worldId;
    private Guid _otherWorldId;

    private Guid _keldaUserId;   // GM
    private Guid _tavrinUserId;  // Player

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();

        _service = new CanonService(_artifactRepo, _factRepo, _relationshipRepo);

        _worldId = Guid.NewGuid();
        _otherWorldId = Guid.NewGuid();
        _keldaUserId = Guid.NewGuid();
        _tavrinUserId = Guid.NewGuid();
    }

    #region Aggregation

    [Test]
    public async Task GetCanonAsync_AggregatesFactsAndRelationshipsAsEntries()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        var harbor = MakeArtifact("Black Harbor", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss, harbor);
        _factRepo.Seed(MakeFact(voss.Id, "denied", "knowing about the caravan", VisibilityScope.PartyVisible, TruthState.Rumor));
        _relationshipRepo.Seed(MakeRelationship(voss.Id, harbor.Id, "LocatedIn", VisibilityScope.PartyVisible, TruthState.Confirmed));

        var result = await _service.GetCanonAsync(Query(_keldaUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!, Has.Count.EqualTo(2));

        var fact = result.Value!.Single(e => e.Kind == CanonEntryKind.Fact);
        Assert.That(fact.ArtifactName, Is.EqualTo("Captain Voss"));
        Assert.That(fact.Label, Is.EqualTo("denied"));
        Assert.That(fact.Detail, Is.EqualTo("knowing about the caravan"));

        var rel = result.Value!.Single(e => e.Kind == CanonEntryKind.Relationship);
        Assert.That(rel.ArtifactName, Is.EqualTo("Captain Voss"));
        Assert.That(rel.OtherArtifactName, Is.EqualTo("Black Harbor"));
        Assert.That(rel.Label, Is.EqualTo("LocatedIn"));
    }

    [Test]
    public async Task GetCanonAsync_OrdersByUpdatedAtDescending()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss);

        var older = MakeFact(voss.Id, "old", "fact", VisibilityScope.PartyVisible, TruthState.Confirmed);
        older.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var newer = MakeFact(voss.Id, "new", "fact", VisibilityScope.PartyVisible, TruthState.Confirmed);
        newer.UpdatedAt = DateTimeOffset.UtcNow;
        _factRepo.Seed(older, newer);

        var result = await _service.GetCanonAsync(Query(_keldaUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.Value!.Select(e => e.Label), Is.EqualTo(new[] { "new", "old" }));
    }

    [Test]
    public async Task GetCanonAsync_EmptyWorld_ReturnsEmpty()
    {
        var result = await _service.GetCanonAsync(Query(_keldaUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!, Is.Empty);
    }

    [Test]
    public async Task GetCanonAsync_ExcludesOtherWorlds()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        var foreign = MakeArtifact("Foreign", VisibilityScope.PartyVisible, worldId: _otherWorldId);
        _artifactRepo.Seed(voss, foreign);
        _factRepo.Seed(
            MakeFact(voss.Id, "here", "value", VisibilityScope.PartyVisible, TruthState.Confirmed),
            MakeFact(foreign.Id, "elsewhere", "value", VisibilityScope.PartyVisible, TruthState.Confirmed));

        var result = await _service.GetCanonAsync(Query(_keldaUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Label, Is.EqualTo("here"));
    }

    #endregion

    #region Visibility scoping

    [Test]
    public async Task GetCanonAsync_Player_ExcludesGmOnlyFacts()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss);
        _factRepo.Seed(
            MakeFact(voss.Id, "public", "in Black Harbor", VisibilityScope.PartyVisible, TruthState.Confirmed),
            MakeFact(voss.Id, "secret", "is a smuggler", VisibilityScope.GMOnly, TruthState.Confirmed));

        var result = await _service.GetCanonAsync(Query(_tavrinUserId, WorldRole.Player), CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Label, Is.EqualTo("public"));
    }

    [Test]
    public async Task GetCanonAsync_Observer_SeesOnlyPartyVisible()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss);
        _factRepo.Seed(
            MakeFact(voss.Id, "public", "value", VisibilityScope.PartyVisible, TruthState.Confirmed),
            MakeFact(voss.Id, "private", "value", VisibilityScope.Private, TruthState.Confirmed));

        var result = await _service.GetCanonAsync(Query(_tavrinUserId, WorldRole.Observer), CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Label, Is.EqualTo("public"));
    }

    [Test]
    public async Task GetCanonAsync_Relationship_ExcludedWhenCounterpartArtifactNotVisible()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        var secretLair = MakeArtifact("Secret Lair", VisibilityScope.GMOnly);
        _artifactRepo.Seed(voss, secretLair);
        _relationshipRepo.Seed(MakeRelationship(voss.Id, secretLair.Id, "Owns", VisibilityScope.PartyVisible, TruthState.Confirmed));

        // The relationship itself is party-visible, but revealing it would expose the GMOnly
        // counterpart, so it must be dropped for the Player.
        var result = await _service.GetCanonAsync(Query(_tavrinUserId, WorldRole.Player), CancellationToken.None);

        Assert.That(result.Value!, Is.Empty);
    }

    #endregion

    #region Hidden truth state

    [Test]
    public async Task GetCanonAsync_HiddenTruthState_ExcludedFromPlayer_EvenWhenPartyVisible()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss);
        // Deliberately party-visible scope but Hidden truth state — must still be GM-only.
        _factRepo.Seed(MakeFact(voss.Id, "truth", "is the traitor", VisibilityScope.PartyVisible, TruthState.Hidden));

        var result = await _service.GetCanonAsync(Query(_tavrinUserId, WorldRole.Player), CancellationToken.None);

        Assert.That(result.Value!, Is.Empty);
    }

    [Test]
    public async Task GetCanonAsync_HiddenTruthState_VisibleToGm()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss);
        _factRepo.Seed(MakeFact(voss.Id, "truth", "is the traitor", VisibilityScope.GMOnly, TruthState.Hidden));

        var result = await _service.GetCanonAsync(Query(_keldaUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].TruthState, Is.EqualTo(TruthState.Hidden));
    }

    #endregion

    #region Truth-state filter

    [Test]
    public async Task GetCanonAsync_TruthStateFilter_ReturnsOnlyMatching()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss);
        _factRepo.Seed(
            MakeFact(voss.Id, "confirmed", "value", VisibilityScope.PartyVisible, TruthState.Confirmed),
            MakeFact(voss.Id, "rumor", "value", VisibilityScope.PartyVisible, TruthState.Rumor),
            MakeFact(voss.Id, "disputed", "value", VisibilityScope.PartyVisible, TruthState.Disputed));

        var query = Query(_keldaUserId, WorldRole.GM) with { TruthState = TruthState.Rumor };

        var result = await _service.GetCanonAsync(query, CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Label, Is.EqualTo("rumor"));
    }

    #endregion

    #region Helpers

    private CanonQuery Query(Guid userId, WorldRole role) =>
        new(_worldId, userId, role);

    private Artifact MakeArtifact(string name, VisibilityScope visibility, Guid? worldId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? _worldId,
            Type = ArtifactType.Character,
            Name = name,
            Visibility = visibility,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static ArtifactFact MakeFact(
        Guid artifactId, string predicate, string value, VisibilityScope visibility, TruthState truthState)
    {
        var now = DateTimeOffset.UtcNow;
        return new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private ArtifactRelationship MakeRelationship(
        Guid aId, Guid bId, string type, VisibilityScope visibility, TruthState truthState)
    {
        var now = DateTimeOffset.UtcNow;
        return new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = aId,
            ArtifactBId = bId,
            Type = type,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    #endregion
}
