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
    private InMemoryCampaignRepository _campaignRepo = null!;
    private InMemoryStorylineCampaignRepository _storylineCampaignRepo = null!;
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
        _campaignRepo = new InMemoryCampaignRepository();
        _storylineCampaignRepo = new InMemoryStorylineCampaignRepository();

        _service = new ArtifactService(_artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo,
            _sourceRepo, new InMemoryCharacterRepository(), new InMemoryWorldMemberRepository(),
            _storylineCampaignRepo, _campaignRepo);

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
    }

    private Campaign SeedCampaign(string name, DateTimeOffset? startedAt = null)
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Name = name,
            StartedAt = startedAt,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _campaignRepo.Seed(campaign);
        return campaign;
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
    public async Task Timeline_HiddenTruthFactsHiddenFromPlayers()
    {
        // Hidden truth states are GM knowledge even when the fact's visibility scope is
        // PartyVisible — the timeline applies the same gate as Ask and Canon.
        var storyline = SeedArtifact("Arc");
        var session = SeedSession("Session", DateTimeOffset.UtcNow.AddDays(-3));
        var hidden = SeedFact(storyline, "twist", "The patron is the villain",
            VisibilityScope.PartyVisible, TruthState.Hidden);
        SeedReference(hidden.Id, SourceReferenceTargetType.ArtifactFact, session);

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
    public async Task Timeline_PartOfBecomesLaneParent_NotLink()
    {
        var parent = SeedArtifact("Main arc");
        var child = SeedArtifact("Sub-arc");
        _relationshipRepo.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = child.Id,
            ArtifactBId = parent.Id,
            Type = ArtifactService.PartOfRelationshipType,
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var childLane = result.Value!.Lanes.Single(l => l.StorylineId == child.Id);
        Assert.That(childLane.ParentStorylineId, Is.EqualTo(parent.Id));
        Assert.That(result.Value.Links, Is.Empty);
    }

    [Test]
    public async Task Timeline_LaneReportsTheCampaignItsSessionsFallIn()
    {
        var storyline = SeedArtifact("Arc");
        var campaign = SeedCampaign("The Throne of Thorns");
        var session = SeedSession("Session", DateTimeOffset.UtcNow.AddDays(-3));
        session.CampaignId = campaign.Id;

        var fact = SeedFact(storyline, "development", "Something happened");
        SeedReference(fact.Id, SourceReferenceTargetType.ArtifactFact, session);

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var lane = result.Value!.Lanes.Single();
        Assert.That(lane.CampaignName, Is.EqualTo("The Throne of Thorns"));
        // Derived from the session, not GM-declared.
        var spanned = lane.Campaigns.Single();
        Assert.That(spanned.CampaignId, Is.EqualTo(campaign.Id));
        Assert.That(spanned.Derived, Is.True);
        Assert.That(spanned.Declared, Is.False);
    }

    [Test]
    public async Task Timeline_LaneCarriesItsCampaignStartDate_SoBandsCanOrderByIt()
    {
        var startedAt = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var storyline = SeedArtifact("Arc");
        var campaign = SeedCampaign("The Throne of Thorns", startedAt);
        // The arc only picks up months after the campaign opened — the band must still
        // sort by the campaign's own start, so the lane has to carry it.
        var session = SeedSession("Session", new DateTimeOffset(2025, 9, 4, 0, 0, 0, TimeSpan.Zero));
        session.CampaignId = campaign.Id;

        var fact = SeedFact(storyline, "development", "Something happened");
        SeedReference(fact.Id, SourceReferenceTargetType.ArtifactFact, session);

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Lanes.Single().CampaignStartedAt, Is.EqualTo(startedAt));
    }

    [Test]
    public async Task Timeline_UndatedCampaignLeavesStartNull()
    {
        var storyline = SeedArtifact("Arc");
        var campaign = SeedCampaign("Unnamed run");
        var session = SeedSession("Session", DateTimeOffset.UtcNow.AddDays(-3));
        session.CampaignId = campaign.Id;

        var fact = SeedFact(storyline, "development", "Something happened");
        SeedReference(fact.Id, SourceReferenceTargetType.ArtifactFact, session);

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Lanes.Single().CampaignStartedAt, Is.Null);
    }

    [Test]
    public async Task Timeline_LanesOpeningTogetherOrderByWhenTheyClose()
    {
        var opening = new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero);
        // Seeded longest-first so a stable sort on the opening date alone would leave
        // them in the wrong order — only the close tie-break can flip them.
        var longArc = SeedArtifact("Long arc");
        var shortArc = SeedArtifact("Short arc");

        var first = SeedSession("Session 1", opening);
        var middle = SeedSession("Session 2", new DateTimeOffset(2025, 2, 14, 0, 0, 0, TimeSpan.Zero));
        var last = SeedSession("Session 3", new DateTimeOffset(2025, 5, 30, 0, 0, 0, TimeSpan.Zero));

        // Both arcs open in the same session; the short one stops moving first.
        foreach (var (arc, second) in new[] { (shortArc, middle), (longArc, last) })
        {
            var openingFact = SeedFact(arc, "development", $"{arc.Name} opens");
            SeedReference(openingFact.Id, SourceReferenceTargetType.ArtifactFact, first);
            var closingFact = SeedFact(arc, "development", $"{arc.Name} closes");
            SeedReference(closingFact.Id, SourceReferenceTargetType.ArtifactFact, second);
        }

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Lanes.Select(l => l.Name), Is.EqualTo(new[] { "Short arc", "Long arc" }));
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

    private void Advance(Artifact storyline, Campaign campaign, string title, DateTimeOffset when)
    {
        var session = SeedSession(title, when);
        session.CampaignId = campaign.Id;
        var fact = SeedFact(storyline, "development", $"{title} happened");
        SeedReference(fact.Id, SourceReferenceTargetType.ArtifactFact, session);
    }

    [Test]
    public async Task Timeline_LaneSpansEveryCampaignItsSessionsFallIn_NoVote()
    {
        var storyline = SeedArtifact("Cross-campaign arc");
        var throne = SeedCampaign("Throne", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var reckoning = SeedCampaign("Reckoning", new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

        // Two sessions in Throne, one in Reckoning — the old majority vote would drop Reckoning.
        Advance(storyline, throne, "T1", new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero));
        Advance(storyline, throne, "T2", new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero));
        Advance(storyline, reckoning, "R1", new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var lane = result.Value!.Lanes.Single();
        Assert.That(lane.Campaigns.Select(c => c.CampaignId), Is.EquivalentTo(new[] { throne.Id, reckoning.Id }));
        Assert.That(lane.Campaigns.All(c => c.Derived), Is.True);
        Assert.That(lane.Campaigns.Any(c => c.Declared), Is.False);
    }

    [Test]
    public async Task Timeline_AnchorIsEarliestOpeningCampaign_NotTheMostFrequent()
    {
        var storyline = SeedArtifact("Arc");
        var early = SeedCampaign("Early", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var late = SeedCampaign("Late", new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

        // One session in Early, two in Late: the majority is Late, but Early opened first.
        Advance(storyline, early, "E1", new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero));
        Advance(storyline, late, "L1", new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero));
        Advance(storyline, late, "L2", new DateTimeOffset(2025, 8, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Lanes.Single().CampaignName, Is.EqualTo("Early"));
    }

    [Test]
    public async Task Timeline_DeclaredCampaignAppearsEvenWithNoSessions()
    {
        var storyline = SeedArtifact("Arc");
        var played = SeedCampaign("Played", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var foreshadowed = SeedCampaign("Foreshadowed", new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));
        _storylineCampaignRepo.Seed(storyline.Id, foreshadowed.Id);

        Advance(storyline, played, "Session", new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var lane = result.Value!.Lanes.Single();
        var declaredOnly = lane.Campaigns.Single(c => c.CampaignId == foreshadowed.Id);
        Assert.That(declaredOnly.Declared, Is.True);
        Assert.That(declaredOnly.Derived, Is.False);

        var playedCampaign = lane.Campaigns.Single(c => c.CampaignId == played.Id);
        Assert.That(playedCampaign.Derived, Is.True);
        Assert.That(playedCampaign.Declared, Is.False);
    }

    [Test]
    public async Task Timeline_PointCarriesItsCampaignId()
    {
        var storyline = SeedArtifact("Arc");
        var campaign = SeedCampaign("Camp");
        Advance(storyline, campaign, "Session", new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await _service.GetStorylineTimelineAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Lanes.Single().Points.Single().CampaignId, Is.EqualTo(campaign.Id));
    }
}
