using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Knowledge;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Knowledge;

/// <summary>
/// Archived artifacts are merge leftovers (or otherwise retired records). They must not
/// surface in ask retrieval — via name-matching or recency — or the Loremaster answers
/// from superseded lore.
/// </summary>
[TestFixture]
public class ArchivedArtifactExclusionTests
{
    private Guid _worldId;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private KeywordKnowledgeRetriever _retriever = null!;

    private Artifact _archived = null!;
    private Artifact _active = null!;

    [SetUp]
    public void SetUp()
    {
        _worldId = Guid.NewGuid();
        _artifactRepo = new InMemoryArtifactRepository();

        // Archived twin: name-matches the question AND is the most recently updated,
        // so it would win both retrieval paths if not excluded.
        _archived = MakeArtifact("Missing Caravan", ArtifactStatus.Archived, DateTimeOffset.UtcNow);
        _active = MakeArtifact("Captain Voss", ArtifactStatus.Active, DateTimeOffset.UtcNow.AddDays(-1));
        _artifactRepo.Seed(_archived, _active);

        var options = Options.Create(new LoremasterOptions
        {
            MaxRetrievalCount = 30,
            MaxFactsPerArtifact = 15
        });

        _retriever = new KeywordKnowledgeRetriever(
            _artifactRepo,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceReferenceRepository(),
            new InMemorySourceRepository(),
            options);
    }

    [TestCase(WorldRole.GM)]
    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task ArchivedArtifacts_AreExcludedFromRetrieval(WorldRole role)
    {
        var context = await _retriever.RetrieveAsync(
            "What happened to the Missing Caravan?", _worldId, Guid.NewGuid(), role, CancellationToken.None);

        Assert.That(context.Artifacts.Select(a => a.Id), Does.Not.Contain(_archived.Id),
            "Archived artifacts must not reach ask retrieval");
        Assert.That(context.Artifacts.Select(a => a.Id), Does.Contain(_active.Id),
            "Active artifacts should still be retrieved via recency");
    }

    private Artifact MakeArtifact(string name, ArtifactStatus status, DateTimeOffset updatedAt) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = _worldId,
        Type = ArtifactType.Character,
        Name = name,
        Summary = $"Test artifact {name}",
        Visibility = VisibilityScope.PartyVisible,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        UpdatedAt = updatedAt,
        RowVersion = []
    };
}
