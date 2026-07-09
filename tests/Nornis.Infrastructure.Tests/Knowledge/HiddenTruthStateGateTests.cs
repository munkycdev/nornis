using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Knowledge;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Knowledge;

/// <summary>
/// TruthState.Hidden is GM-only truth regardless of the visibility scope carried by the
/// fact or relationship itself (parity with CanonService). The retriever must never feed
/// Hidden knowledge into a Player or Observer ask.
/// </summary>
[TestFixture]
public class HiddenTruthStateGateTests
{
    private Guid _worldId;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private KeywordKnowledgeRetriever _retriever = null!;

    private Artifact _voss = null!;
    private Artifact _harbor = null!;

    [SetUp]
    public void SetUp()
    {
        _worldId = Guid.NewGuid();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();

        _voss = MakeArtifact("Captain Voss", ArtifactType.Character);
        _harbor = MakeArtifact("Black Harbor", ArtifactType.Location);
        _artifactRepo.Seed(_voss, _harbor);

        // A PartyVisible fact whose truth state is Hidden — visibility alone would let it
        // through, so the truth-state gate must catch it.
        _factRepo.Seed(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = _voss.Id,
            Predicate = "true allegiance",
            Value = "The Shadow Guild",
            TruthState = TruthState.Hidden,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        _relationshipRepo.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = _voss.Id,
            ArtifactBId = _harbor.Id,
            Type = "SecretlyControls",
            TruthState = TruthState.Hidden,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var options = Options.Create(new LoremasterOptions
        {
            MaxRetrievalCount = 30,
            MaxFactsPerArtifact = 15
        });

        _retriever = new KeywordKnowledgeRetriever(
            _artifactRepo, _factRepo, _relationshipRepo, new InMemorySourceReferenceRepository(), options);
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task HiddenTruthState_IsFilteredForNonGmRoles(WorldRole role)
    {
        var context = await _retriever.RetrieveAsync(
            "What do we know about Captain Voss?", _worldId, Guid.NewGuid(), role, CancellationToken.None);

        Assert.That(context.Facts, Is.Empty, "Hidden facts must not reach non-GM askers");
        Assert.That(context.Relationships, Is.Empty, "Hidden relationships must not reach non-GM askers");
    }

    [Test]
    public async Task HiddenTruthState_IsIncludedForGm()
    {
        var context = await _retriever.RetrieveAsync(
            "What do we know about Captain Voss?", _worldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(context.Facts, Has.Count.EqualTo(1));
        Assert.That(context.Facts[0].TruthState, Is.EqualTo(TruthState.Hidden));
        Assert.That(context.Relationships, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RetrievedArtifacts_CarryStatus()
    {
        var context = await _retriever.RetrieveAsync(
            "Tell me about Captain Voss", _worldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        var voss = context.Artifacts.Single(a => a.Id == _voss.Id);
        Assert.That(voss.Status, Is.EqualTo("Active"));
    }

    private Artifact MakeArtifact(string name, ArtifactType type) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = _worldId,
        Type = type,
        Name = name,
        Summary = $"Test artifact {name}",
        Visibility = VisibilityScope.PartyVisible,
        Status = ArtifactStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        RowVersion = []
    };
}
