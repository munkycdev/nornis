using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class SuggestionServiceTests
{
    private Guid _worldId;
    private Guid _userId;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private SuggestionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _worldId = Guid.NewGuid();
        _userId = Guid.NewGuid();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _sourceRepo = new InMemorySourceRepository();
        _service = new SuggestionService(_artifactRepo, _factRepo, _sourceRepo);
    }

    private Artifact SeedArtifact(
        string name,
        ArtifactType type = ArtifactType.Character,
        ArtifactStatus status = ArtifactStatus.Active,
        VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Status = status,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private Task<IReadOnlyList<Nornis.Application.Models.AskSuggestion>> GetAsync(WorldRole role = WorldRole.GM) =>
        _service.GetSuggestionsAsync(_worldId, _userId, role, CancellationToken.None);

    [Test]
    public async Task EmptyWorld_ReturnsFourFallbacks()
    {
        var suggestions = await GetAsync();

        Assert.That(suggestions, Has.Count.EqualTo(SuggestionService.SuggestionCount));
        Assert.That(suggestions.Select(s => s.Category), Is.All.EqualTo("recap"));
        Assert.That(suggestions.Select(s => s.Text), Is.Unique);
    }

    [Test]
    public async Task AlwaysReturnsExactlyFour()
    {
        SeedArtifact("The Missing Caravan", ArtifactType.Storyline);
        SeedArtifact("Captain Voss");
        SeedArtifact("Black Harbor", ArtifactType.Location);

        var suggestions = await GetAsync();

        Assert.That(suggestions, Has.Count.EqualTo(SuggestionService.SuggestionCount));
        Assert.That(suggestions.Select(s => s.Text), Is.Unique);
    }

    [Test]
    public async Task ActiveStoryline_ProducesStorylineSuggestionFirst()
    {
        SeedArtifact("The Missing Caravan", ArtifactType.Storyline);

        var suggestions = await GetAsync();

        Assert.That(suggestions[0].Category, Is.EqualTo("storyline"));
        Assert.That(suggestions[0].Text, Does.Contain("The Missing Caravan"));
    }

    [Test]
    public async Task DormantStoryline_AsksAboutUnresolvedBusiness()
    {
        SeedArtifact("The Hollow Prophecy", ArtifactType.Storyline, ArtifactStatus.Dormant);

        var suggestions = await GetAsync();

        Assert.That(suggestions[0].Text, Is.EqualTo("What was left unresolved in The Hollow Prophecy?"));
    }

    [Test]
    public async Task ResolvedAndArchivedStorylines_ProduceNoStorylineSuggestion()
    {
        SeedArtifact("Done Deal", ArtifactType.Storyline, ArtifactStatus.Resolved);
        SeedArtifact("Old News", ArtifactType.Storyline, ArtifactStatus.Archived);

        var suggestions = await GetAsync();

        Assert.That(suggestions.Select(s => s.Category), Does.Not.Contain("storyline"));
    }

    [Test]
    public async Task Character_ProducesCharacterSuggestion()
    {
        SeedArtifact("Captain Voss");

        var suggestions = await GetAsync();

        var character = suggestions.SingleOrDefault(s => s.Category == "character");
        Assert.That(character, Is.Not.Null);
        Assert.That(character!.Text, Does.Contain("Captain Voss"));
    }

    [Test]
    public async Task RumorFacts_ProduceRumorSuggestion()
    {
        var voss = SeedArtifact("Captain Voss");
        _factRepo.Seed(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = voss.Id,
            Predicate = "suspected in",
            Value = "Missing Caravan",
            TruthState = TruthState.Rumor,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var suggestions = await GetAsync();

        Assert.That(suggestions.Select(s => s.Category), Does.Contain("rumor"));
    }

    [Test]
    public async Task ConfirmedFactsOnly_NoRumorSuggestion()
    {
        var voss = SeedArtifact("Captain Voss");
        _factRepo.Seed(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = voss.Id,
            Predicate = "location",
            Value = "Black Harbor",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var suggestions = await GetAsync();

        Assert.That(suggestions.Select(s => s.Category), Does.Not.Contain("rumor"));
    }

    [Test]
    public async Task GmOnlyArtifactNames_NeverAppearForObservers()
    {
        SeedArtifact("Secret Villain", ArtifactType.Character, visibility: VisibilityScope.GMOnly);
        SeedArtifact("Hidden Plot", ArtifactType.Storyline, visibility: VisibilityScope.GMOnly);

        var suggestions = await GetAsync(WorldRole.Observer);

        Assert.That(suggestions.Select(s => s.Text), Has.None.Contains("Secret Villain"));
        Assert.That(suggestions.Select(s => s.Text), Has.None.Contains("Hidden Plot"));
    }

    [Test]
    public async Task RecentSource_ProducesRecapSuggestion()
    {
        _sourceRepo.Seed(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 12: The Docks",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });

        var suggestions = await GetAsync();

        Assert.That(suggestions.Select(s => s.Text),
            Does.Contain("What changed since \"Session 12: The Docks\"?"));
    }

    [Test]
    public async Task OldSource_ProducesNoRecapSuggestion()
    {
        _sourceRepo.Seed(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Ancient History",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        });

        var suggestions = await GetAsync();

        Assert.That(suggestions.Select(s => s.Text), Has.None.Contains("Ancient History"));
    }

    [Test]
    public async Task OtherUsersPrivateSource_NotShownToPlayer()
    {
        _sourceRepo.Seed(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.JournalEntry,
            Title = "Someone Else's Diary",
            Visibility = VisibilityScope.Private,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        var suggestions = await GetAsync(WorldRole.Player);

        Assert.That(suggestions.Select(s => s.Text), Has.None.Contains("Someone Else's Diary"));
    }

    [Test]
    public async Task SameDay_ReturnsSameSuggestions()
    {
        SeedArtifact("The Missing Caravan", ArtifactType.Storyline);
        SeedArtifact("Captain Voss");

        var first = await GetAsync();
        var second = await GetAsync();

        Assert.That(first.Select(s => s.Text), Is.EqualTo(second.Select(s => s.Text)));
    }

    [Test]
    public void DaySeed_IsStableForWorldAndDate()
    {
        var worldId = Guid.NewGuid();
        var date = new DateOnly(2026, 7, 7);

        Assert.That(SuggestionService.DaySeed(worldId, date),
            Is.EqualTo(SuggestionService.DaySeed(worldId, date)));
        Assert.That(SuggestionService.DaySeed(worldId, date),
            Is.Not.EqualTo(SuggestionService.DaySeed(worldId, date.AddDays(1))));
    }
}
