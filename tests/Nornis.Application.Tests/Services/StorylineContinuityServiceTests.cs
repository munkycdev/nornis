using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class StorylineContinuityServiceTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;

    private Guid _worldId;
    private Guid _gmUserId;

    private static readonly DateTimeOffset Base = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
    }

    private StorylineContinuityService Service(int staleThreshold = 3)
    {
        var reader = new StorylineDevelopmentReader(
            _artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo, _sourceRepo);
        return new StorylineContinuityService(reader, Options.Create(new ContinuityOptions
        {
            StaleThresholdSessions = staleThreshold,
            RecentSessionWindow = 2
        }));
    }

    private Artifact SeedStoryline(
        string name,
        ArtifactStatus status = ArtifactStatus.Active,
        VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var storyline = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Storyline,
            Name = name,
            Visibility = visibility,
            Status = status,
            CreatedAt = Base,
            UpdatedAt = Base
        };
        _artifactRepo.Seed(storyline);
        return storyline;
    }

    private Source SeedSession(int dayOffset, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = $"Session +{dayOffset}",
            Body = "…",
            Visibility = visibility,
            OccurredAt = Base.AddDays(dayOffset),
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = Base.AddDays(dayOffset),
            CreatedByUserId = _gmUserId
        };
        _sourceRepo.Seed(source);
        return source;
    }

    // A development on a storyline at a session: a dated fact citation attributes the
    // storyline to that session, exactly as the timeline reads it.
    private void SeedDevelopment(Artifact storyline, Source session,
        VisibilityScope visibility = VisibilityScope.PartyVisible, TruthState truthState = TruthState.Confirmed,
        string predicate = "development", string value = "something happened")
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = storyline.Id,
            Predicate = predicate,
            Value = value,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = session.OccurredAt!.Value,
            UpdatedAt = session.OccurredAt!.Value
        };
        _factRepo.Seed(fact);
        _sourceRefRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = session.Id,
            TargetType = SourceReferenceTargetType.ArtifactFact,
            TargetId = fact.Id,
            CreatedAt = session.OccurredAt!.Value
        });
    }

    private void SeedOpenQuestion(Artifact storyline, Source session, TruthState truthState = TruthState.Likely) =>
        SeedDevelopment(storyline, session, predicate: "open question", value: "Who did it?", truthState: truthState);

    [Test]
    public async Task EmptyWorld_ReturnsEmptyReport()
    {
        var result = await Service().GetContinuityReportAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ActiveCount, Is.EqualTo(0));
        Assert.That(result.Value.Quiet, Is.Empty);
        Assert.That(result.Value.Unanchored, Is.Empty);
        Assert.That(result.Value.LatestSession, Is.Null);
    }

    [Test]
    public async Task StorylineTouchedInLatestSession_IsNotQuiet()
    {
        var arc = SeedStoryline("Live Arc");
        var s1 = SeedSession(1);
        var s2 = SeedSession(10);
        SeedDevelopment(arc, s1);
        SeedDevelopment(arc, s2); // touched in the latest session

        var result = await Service(staleThreshold: 1).GetContinuityReportAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Quiet, Is.Empty);
        Assert.That(result.Value.ActiveCount, Is.EqualTo(1));
        Assert.That(result.Value.LatestSession!.OccurredAt, Is.EqualTo(Base.AddDays(10)));
    }

    [TestCase(2, false)] // 2 sessions since last touch, threshold 3 → not quiet
    [TestCase(3, true)]  // 3 sessions since → quiet
    [TestCase(4, true)]
    public async Task StalenessThreshold_IsCountedInSessions(int sessionsAfter, bool expectedQuiet)
    {
        var arc = SeedStoryline("Quiet Arc");
        var other = SeedStoryline("Busy Arc");

        var first = SeedSession(1);
        SeedDevelopment(arc, first);

        // Each later session touches a *different* storyline, so time passes for "arc".
        for (var i = 1; i <= sessionsAfter; i++)
        {
            SeedDevelopment(other, SeedSession(1 + i * 10));
        }

        var result = await Service(staleThreshold: 3).GetContinuityReportAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var quiet = result.Value!.Quiet.SingleOrDefault(q => q.Name == "Quiet Arc");
        if (expectedQuiet)
        {
            Assert.That(quiet, Is.Not.Null);
            Assert.That(quiet!.SessionsSinceLastDevelopment, Is.EqualTo(sessionsAfter));
        }
        else
        {
            Assert.That(quiet, Is.Null);
        }
    }

    [Test]
    public async Task ActiveStorylineWithNoDatedDevelopment_IsUnanchoredNotQuiet()
    {
        SeedStoryline("Never Advanced");
        // A busy neighbour so sessions exist, but nothing touches "Never Advanced".
        var busy = SeedStoryline("Busy");
        SeedDevelopment(busy, SeedSession(1));
        SeedDevelopment(busy, SeedSession(20));
        SeedDevelopment(busy, SeedSession(40));
        SeedDevelopment(busy, SeedSession(60));

        var result = await Service().GetContinuityReportAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Quiet.Any(q => q.Name == "Never Advanced"), Is.False);
        Assert.That(result.Value.Unanchored.Select(u => u.Name), Does.Contain("Never Advanced"));
    }

    [Test]
    public async Task ResolvedStorylines_AreExcludedFromActiveAndQuiet()
    {
        var resolved = SeedStoryline("Done", ArtifactStatus.Resolved);
        SeedDevelopment(resolved, SeedSession(1));
        // enough later sessions to make it "stale" were it Active
        var busy = SeedStoryline("Busy");
        SeedDevelopment(busy, SeedSession(20));
        SeedDevelopment(busy, SeedSession(40));
        SeedDevelopment(busy, SeedSession(60));

        var result = await Service().GetContinuityReportAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Quiet.Any(q => q.Name == "Done"), Is.False);
        Assert.That(result.Value.Unanchored.Any(u => u.Name == "Done"), Is.False);
        Assert.That(result.Value.ActiveCount, Is.EqualTo(1)); // only "Busy"
    }

    [Test]
    public async Task QuietStoryline_CountsUnresolvedOpenQuestions()
    {
        var arc = SeedStoryline("Mystery");
        var s1 = SeedSession(1);
        SeedDevelopment(arc, s1);
        SeedOpenQuestion(arc, s1);                             // counts
        SeedOpenQuestion(arc, s1, truthState: TruthState.False); // answered → excluded

        for (var i = 1; i <= 3; i++)
        {
            SeedDevelopment(SeedStoryline($"Filler {i}"), SeedSession(1 + i * 10));
        }

        var result = await Service(staleThreshold: 3).GetContinuityReportAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var mystery = result.Value!.Quiet.Single(q => q.Name == "Mystery");
        Assert.That(mystery.OpenQuestionCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Quiet_IsOrderedByStalenessThenOpenQuestions()
    {
        // Two arcs equally stale; the one with more open questions ranks first.
        var loud = SeedStoryline("Fewer Questions");
        var loose = SeedStoryline("More Questions");
        var first = SeedSession(1);
        SeedDevelopment(loud, first);
        SeedDevelopment(loose, first);
        SeedOpenQuestion(loose, first);
        SeedOpenQuestion(loose, first); // will collapse to one dev, but two facts → two open questions

        for (var i = 1; i <= 3; i++)
        {
            SeedDevelopment(SeedStoryline($"Filler {i}"), SeedSession(1 + i * 10));
        }

        var result = await Service(staleThreshold: 3).GetContinuityReportAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var quiet = result.Value!.Quiet;
        var loudIdx = quiet.ToList().FindIndex(q => q.Name == "Fewer Questions");
        var looseIdx = quiet.ToList().FindIndex(q => q.Name == "More Questions");
        Assert.That(looseIdx, Is.LessThan(loudIdx));
    }

    [Test]
    public async Task GmOnlyStoryline_IsInvisibleToPlayers()
    {
        var secret = SeedStoryline("Hidden Plot", visibility: VisibilityScope.GMOnly);
        SeedDevelopment(secret, SeedSession(1), visibility: VisibilityScope.GMOnly);
        // Party-visible sessions advance time.
        var busy = SeedStoryline("Public Arc");
        for (var i = 1; i <= 3; i++)
        {
            SeedDevelopment(busy, SeedSession(1 + i * 10));
        }

        var playerId = Guid.NewGuid();
        var asPlayer = await Service(staleThreshold: 3)
            .GetContinuityReportAsync(_worldId, playerId, WorldRole.Player, CancellationToken.None);
        var asGm = await Service(staleThreshold: 3)
            .GetContinuityReportAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(asPlayer.Value!.Quiet.Any(q => q.Name == "Hidden Plot"), Is.False);
        Assert.That(asPlayer.Value.Unanchored.Any(u => u.Name == "Hidden Plot"), Is.False);
        // GM sees the hidden arc as quiet.
        Assert.That(asGm.Value!.Quiet.Any(q => q.Name == "Hidden Plot"), Is.True);
    }
}
