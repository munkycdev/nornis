using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ArtifactServiceTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemoryCharacterRepository _characterRepo = null!;
    private InMemoryWorldMemberRepository _memberRepo = null!;
    private ArtifactService _service = null!;

    // World: "Black Harbor Investigation"
    private Guid _worldId;
    private Guid _otherWorldId;

    // Users
    private Guid _keldaUserId;   // GM
    private Guid _tavrinUserId;  // Player

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();

        _characterRepo = new InMemoryCharacterRepository();
        _memberRepo = new InMemoryWorldMemberRepository();
        _service = new ArtifactService(_artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo,
            new InMemorySourceRepository(), _characterRepo, _memberRepo);

        _worldId = Guid.NewGuid();
        _otherWorldId = Guid.NewGuid();
        _keldaUserId = Guid.NewGuid();
        _tavrinUserId = Guid.NewGuid();
    }

    #region ListAsync — visibility scoping

    [Test]
    public async Task ListAsync_Gm_SeesAllVisibilityScopes()
    {
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible),
            MakeArtifact("Hidden Ledger", VisibilityScope.GMOnly),
            MakeArtifact("Kelda's Private Note", VisibilityScope.Private));

        var query = new ArtifactListQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ListAsync_Player_SeesPartyAndPrivateButNotGmOnly()
    {
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible),
            MakeArtifact("Hidden Ledger", VisibilityScope.GMOnly),
            MakeArtifact("Party Private Note", VisibilityScope.Private, createdByUserId: _tavrinUserId));

        var query = new ArtifactListQuery(_worldId, _tavrinUserId, WorldRole.Player);

        var result = await _service.ListAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var names = result.Value!.Select(a => a.Name).ToList();
        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names, Does.Contain("Captain Voss"));
        Assert.That(names, Does.Contain("Party Private Note"));
        Assert.That(names, Does.Not.Contain("Hidden Ledger"));
    }

    [Test]
    public async Task ListAsync_Observer_SeesOnlyPartyVisible()
    {
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible),
            MakeArtifact("Hidden Ledger", VisibilityScope.GMOnly),
            MakeArtifact("Private Note", VisibilityScope.Private));

        var query = new ArtifactListQuery(_worldId, _tavrinUserId, WorldRole.Observer);

        var result = await _service.ListAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Name, Is.EqualTo("Captain Voss"));
    }

    #endregion

    #region ListAsync — filters, ordering, isolation

    [Test]
    public async Task ListAsync_OrdersByUpdatedAtDescending()
    {
        var older = MakeArtifact("Black Harbor", VisibilityScope.PartyVisible);
        older.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var newer = MakeArtifact("Silver Key", VisibilityScope.PartyVisible);
        newer.UpdatedAt = DateTimeOffset.UtcNow;
        _artifactRepo.Seed(older, newer);

        var query = new ArtifactListQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListAsync(query, CancellationToken.None);

        Assert.That(result.Value!.Select(a => a.Name), Is.EqualTo(new[] { "Silver Key", "Black Harbor" }));
    }

    [Test]
    public async Task ListAsync_FiltersByType()
    {
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible, ArtifactType.Character),
            MakeArtifact("Missing Caravan", VisibilityScope.PartyVisible, ArtifactType.Storyline));

        var query = new ArtifactListQuery(_worldId, _keldaUserId, WorldRole.GM, Type: ArtifactType.Storyline);

        var result = await _service.ListAsync(query, CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Name, Is.EqualTo("Missing Caravan"));
    }

    [Test]
    public async Task ListAsync_FiltersByStatus()
    {
        _artifactRepo.Seed(
            MakeArtifact("Active Plot", VisibilityScope.PartyVisible, status: ArtifactStatus.Active),
            MakeArtifact("Resolved Plot", VisibilityScope.PartyVisible, status: ArtifactStatus.Resolved));

        var query = new ArtifactListQuery(_worldId, _keldaUserId, WorldRole.GM, Status: ArtifactStatus.Resolved);

        var result = await _service.ListAsync(query, CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Name, Is.EqualTo("Resolved Plot"));
    }

    [Test]
    public async Task ListAsync_ExcludesOtherWorlds()
    {
        _artifactRepo.Seed(MakeArtifact("Captain Voss", VisibilityScope.PartyVisible));
        _artifactRepo.Seed(MakeArtifact("Foreign Artifact", VisibilityScope.PartyVisible, worldId: _otherWorldId));

        var query = new ArtifactListQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListAsync(query, CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Name, Is.EqualTo("Captain Voss"));
    }

    #endregion

    #region GetDetailAsync — not found / visibility

    [Test]
    public async Task GetDetailAsync_ReturnsNotFound_WhenMissing()
    {
        var result = await _service.GetDetailAsync(
            Guid.NewGuid(), _worldId, _keldaUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task GetDetailAsync_ReturnsNotFound_WhenBelongsToAnotherWorld()
    {
        var foreign = MakeArtifact("Foreign Artifact", VisibilityScope.PartyVisible, worldId: _otherWorldId);
        _artifactRepo.Seed(foreign);

        var result = await _service.GetDetailAsync(
            foreign.Id, _worldId, _keldaUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task GetDetailAsync_ReturnsNotFound_WhenInvisibleToRole()
    {
        var gmOnly = MakeArtifact("Hidden Ledger", VisibilityScope.GMOnly);
        _artifactRepo.Seed(gmOnly);

        // Player cannot see a GMOnly artifact — must not even learn it exists.
        var result = await _service.GetDetailAsync(
            gmOnly.Id, _worldId, _tavrinUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    #endregion

    #region GetDetailAsync — aggregation and fact/relationship visibility

    [Test]
    public async Task GetDetailAsync_FiltersGmOnlyFactsFromPlayer()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss);
        _factRepo.Seed(
            MakeFact(voss.Id, "location", "Black Harbor", VisibilityScope.PartyVisible),
            MakeFact(voss.Id, "secret", "Is a smuggler", VisibilityScope.GMOnly));

        var result = await _service.GetDetailAsync(
            voss.Id, _worldId, _tavrinUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Facts, Has.Count.EqualTo(1));
        Assert.That(result.Value.Facts[0].Value, Is.EqualTo("Black Harbor"));
    }

    [Test]
    public async Task GetDetailAsync_Gm_SeesGmOnlyFacts()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss);
        _factRepo.Seed(
            MakeFact(voss.Id, "location", "Black Harbor", VisibilityScope.PartyVisible),
            MakeFact(voss.Id, "secret", "Is a smuggler", VisibilityScope.GMOnly));

        var result = await _service.GetDetailAsync(
            voss.Id, _worldId, _keldaUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Facts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetDetailAsync_ResolvesConnectedArtifacts_AndFiltersInvisibleCounterparts()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        var harbor = MakeArtifact("Black Harbor", VisibilityScope.PartyVisible);
        var secretLair = MakeArtifact("Secret Lair", VisibilityScope.GMOnly);
        _artifactRepo.Seed(voss, harbor, secretLair);

        _relationshipRepo.Seed(
            MakeRelationship(voss.Id, harbor.Id, "LocatedIn", VisibilityScope.PartyVisible),
            MakeRelationship(voss.Id, secretLair.Id, "Owns", VisibilityScope.PartyVisible));

        // Player: relationships are party-visible, but the Secret Lair counterpart is GMOnly
        // and must be dropped from the connected list.
        var result = await _service.GetDetailAsync(
            voss.Id, _worldId, _tavrinUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Value!.Relationships, Has.Count.EqualTo(2));
        var connectedNames = result.Value.ConnectedArtifacts.Select(a => a.Name).ToList();
        Assert.That(connectedNames, Has.Count.EqualTo(1));
        Assert.That(connectedNames, Does.Contain("Black Harbor"));
        Assert.That(connectedNames, Does.Not.Contain("Secret Lair"));
    }

    [Test]
    public async Task GetDetailAsync_IncludesSourceReferencesForArtifactFactsAndRelationships()
    {
        var voss = MakeArtifact("Captain Voss", VisibilityScope.PartyVisible);
        var harbor = MakeArtifact("Black Harbor", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(voss, harbor);

        var fact = MakeFact(voss.Id, "location", "Black Harbor", VisibilityScope.PartyVisible);
        _factRepo.Seed(fact);
        var rel = MakeRelationship(voss.Id, harbor.Id, "LocatedIn", VisibilityScope.PartyVisible);
        _relationshipRepo.Seed(rel);

        var sourceId = Guid.NewGuid();
        _sourceRefRepo.CreateAsync(MakeSourceRef(sourceId, SourceReferenceTargetType.Artifact, voss.Id)).Wait();
        _sourceRefRepo.CreateAsync(MakeSourceRef(sourceId, SourceReferenceTargetType.ArtifactFact, fact.Id)).Wait();
        _sourceRefRepo.CreateAsync(MakeSourceRef(sourceId, SourceReferenceTargetType.ArtifactRelationship, rel.Id)).Wait();

        var result = await _service.GetDetailAsync(
            voss.Id, _worldId, _keldaUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.SourceReferences, Has.Count.EqualTo(3));
    }

    #endregion

    #region Helpers

    #region GetDetailAsync — played by

    private async Task<WorldMember> SeedMember(string? displayName)
    {
        return await _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = Guid.NewGuid(),
            Role = WorldRole.Player,
            DisplayName = displayName,
            JoinedAt = DateTimeOffset.UtcNow
        });
    }

    private void SeedLinkedCharacter(WorldMember owner, Guid artifactId, string name = "Ugma")
    {
        _characterRepo.Seed(new Character
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            WorldMemberId = owner.Id,
            Name = name,
            ArtifactId = artifactId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    [Test]
    public async Task GetDetailAsync_CharacterArtifactWithLinkedCharacter_NamesThePlayer()
    {
        var artifact = MakeArtifact("Ugma", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(artifact);
        var member = await SeedMember("Dave");
        SeedLinkedCharacter(member, artifact.Id);

        var result = await _service.GetDetailAsync(artifact.Id, _worldId, _tavrinUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PlayedBy, Is.EqualTo(new[] { "Dave" }));
    }

    [Test]
    public async Task GetDetailAsync_MemberWithoutDisplayName_FallsBackToUserId()
    {
        var artifact = MakeArtifact("Ugma", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(artifact);
        var member = await SeedMember(null);
        SeedLinkedCharacter(member, artifact.Id);

        var result = await _service.GetDetailAsync(artifact.Id, _worldId, _tavrinUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PlayedBy, Has.Count.EqualTo(1));
        Assert.That(result.Value.PlayedBy[0], Does.StartWith("User "));
    }

    [Test]
    public async Task GetDetailAsync_UnlinkedCharacterArtifact_HasEmptyPlayedBy()
    {
        var artifact = MakeArtifact("Dagasto", VisibilityScope.PartyVisible);
        _artifactRepo.Seed(artifact);

        var result = await _service.GetDetailAsync(artifact.Id, _worldId, _tavrinUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PlayedBy, Is.Empty);
    }

    [Test]
    public async Task GetDetailAsync_NonCharacterArtifact_HasEmptyPlayedBy()
    {
        var artifact = MakeArtifact("Black Harbor", VisibilityScope.PartyVisible, type: ArtifactType.Location);
        _artifactRepo.Seed(artifact);

        var result = await _service.GetDetailAsync(artifact.Id, _worldId, _tavrinUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PlayedBy, Is.Empty);
    }

    #endregion

    private Artifact MakeArtifact(
        string name,
        VisibilityScope visibility,
        ArtifactType type = ArtifactType.Character,
        ArtifactStatus status = ArtifactStatus.Active,
        Guid? worldId = null,
        Guid? createdByUserId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? _worldId,
            CreatedByUserId = createdByUserId,
            Type = type,
            Name = name,
            Summary = null,
            Visibility = visibility,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static ArtifactFact MakeFact(Guid artifactId, string predicate, string value, VisibilityScope visibility)
    {
        var now = DateTimeOffset.UtcNow;
        return new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            TruthState = TruthState.Confirmed,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private ArtifactRelationship MakeRelationship(Guid aId, Guid bId, string type, VisibilityScope visibility)
    {
        var now = DateTimeOffset.UtcNow;
        return new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = aId,
            ArtifactBId = bId,
            Type = type,
            TruthState = TruthState.Confirmed,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static SourceReference MakeSourceRef(Guid sourceId, SourceReferenceTargetType targetType, Guid targetId)
    {
        return new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
