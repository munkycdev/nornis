using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ArtifactServiceTimelineTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private ArtifactService _service = null!;

    private Guid _worldId;
    private Guid _gmUserId;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();

        _service = new ArtifactService(_artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo,
            _sourceRepo, new InMemoryCharacterRepository(), new InMemoryWorldMemberRepository());

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
    }

    private Artifact SeedArtifact(
        string name,
        ArtifactType type = ArtifactType.Storyline,
        ArtifactStatus status = ArtifactStatus.Active,
        VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Visibility = visibility,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private Source SeedSession(string title, DateTimeOffset? occurredAt, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = title,
            Body = "…",
            Visibility = visibility,
            OccurredAt = occurredAt,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _gmUserId
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private ArtifactFact SeedFact(Artifact artifact, string predicate, string value,
        VisibilityScope visibility = VisibilityScope.PartyVisible, TruthState truthState = TruthState.Confirmed)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = predicate,
            Value = value,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _factRepo.Seed(fact);
        return fact;
    }

    private void SeedReference(Guid targetId, SourceReferenceTargetType targetType, Source source, string? quote = null)
    {
        _sourceRefRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TargetType = targetType,
            TargetId = targetId,
            Quote = quote,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    [Test]
    public async Task Timeline_GroupsDatedFactsIntoLanePoints()
    {
        var storyline = SeedArtifact("Search for Spider Bane");
        var session1 = SeedSession("Session 1", new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero));
        var session2 = SeedSession("Session 2", new DateTimeOffset(2025, 2, 14, 0, 0, 0, TimeSpan.Zero));

        var fact1 = SeedFact(storyline, "development", "The trail begins");
        var fact2 = SeedFact(storyline, "development", "A clue is found");
        SeedReference(fact1.Id, SourceReferenceTargetType.ArtifactFact, session1, "quote one");
        SeedReference(fact2.Id, SourceReferenceTargetType.ArtifactFact, session2);

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var lane = result.Value!.Lanes.Single();
        Assert.That(lane.Name, Is.EqualTo("Search for Spider Bane"));
        Assert.That(lane.Points, Has.Count.EqualTo(2));
        Assert.That(lane.Points[0].OccurredAt.Month, Is.EqualTo(1));
        Assert.That(lane.Points[0].Developments.Single().Quote, Is.EqualTo("quote one"));
        Assert.That(result.Value.Sessions, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Timeline_UndatedSourcesAreExcluded()
    {
        var storyline = SeedArtifact("Arc");
        var loreDoc = SeedSession("Setting primer", occurredAt: null);
        var fact = SeedFact(storyline, "development", "Background lore");
        SeedReference(fact.Id, SourceReferenceTargetType.ArtifactFact, loreDoc);

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Lanes.Single().Points, Is.Empty);
        Assert.That(result.Value.Sessions, Is.Empty);
    }

    [Test]
    public async Task Timeline_OpenQuestionFactsAreFlagged()
    {
        var storyline = SeedArtifact("Arc");
        var session = SeedSession("Session", DateTimeOffset.UtcNow.AddDays(-3));
        var question = SeedFact(storyline, "open question", "Who hired the raiders?");
        SeedReference(question.Id, SourceReferenceTargetType.ArtifactFact, session);

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var development = result.Value!.Lanes.Single().Points.Single().Developments.Single();
        Assert.That(development.IsOpenQuestion, Is.True);
    }

    [Test]
    public async Task Timeline_GmOnlyFactsHiddenFromPlayers()
    {
        var storyline = SeedArtifact("Arc");
        var session = SeedSession("Session", DateTimeOffset.UtcNow.AddDays(-3));
        var secret = SeedFact(storyline, "secret", "The duke is a doppelganger", VisibilityScope.GMOnly);
        SeedReference(secret.Id, SourceReferenceTargetType.ArtifactFact, session);

        var asPlayer = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.Player, CancellationToken.None);
        var asGm = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(asPlayer.Value!.Lanes.Single().Points, Is.Empty);
        Assert.That(asGm.Value!.Lanes.Single().Points, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Timeline_ArchivedStorylinesExcluded()
    {
        SeedArtifact("Merged leftover", status: ArtifactStatus.Archived);
        SeedArtifact("Live arc");

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Lanes.Select(l => l.Name), Is.EqualTo(new[] { "Live arc" }));
    }

    [Test]
    public async Task Timeline_StorylineToStorylineRelationshipsBecomeLinks()
    {
        var arcA = SeedArtifact("Arc A");
        var arcB = SeedArtifact("Arc B");
        _relationshipRepo.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = arcA.Id,
            ArtifactBId = arcB.Id,
            Type = "SpawnedFrom",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var link = result.Value!.Links.Single();
        Assert.That(link.FromStorylineId, Is.EqualTo(arcA.Id));
        Assert.That(link.ToStorylineId, Is.EqualTo(arcB.Id));
        Assert.That(link.Type, Is.EqualTo("SpawnedFrom"));
    }

    [Test]
    public async Task Timeline_SessionTouchingTwoStorylinesCountsBoth()
    {
        var arcA = SeedArtifact("Arc A");
        var arcB = SeedArtifact("Arc B");
        var session = SeedSession("Big session", DateTimeOffset.UtcNow.AddDays(-5));

        var factA = SeedFact(arcA, "development", "A moves");
        var factB = SeedFact(arcB, "development", "B moves");
        SeedReference(factA.Id, SourceReferenceTargetType.ArtifactFact, session);
        SeedReference(factB.Id, SourceReferenceTargetType.ArtifactFact, session);

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Sessions.Single().StorylineCount, Is.EqualTo(2));
    }
}
