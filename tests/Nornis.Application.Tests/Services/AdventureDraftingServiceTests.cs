using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Knowledge;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Prep's drafting (feature 29, design "Drafting"): what refuses a draft before it starts, what a
/// finished draft writes, what a cut-short one doesn't, the revision bound, Undo, the ledger row,
/// and that the GM's prompt carries Hidden truth that the same retrieval at Player role doesn't.
/// </summary>
[TestFixture]
public class AdventureDraftingServiceTests
{
    private static readonly Guid GmUserId = Guid.NewGuid();

    private Guid _worldId;
    private InMemoryAdventureRepository _adventures = null!;
    private InMemoryAiUsageRecordRepository _ledger = null!;
    private InMemoryArtifactRepository _artifacts = null!;
    private InMemoryArtifactFactRepository _facts = null!;
    private SpyKnowledgeRetriever _retriever = null!;
    private FakePrepAiClient _ai = null!;
    private FakeAiBudgetGuard _budget = null!;
    private SectionDraftGate _gate = null!;
    private StubConvergenceGauge _gauge = null!;
    private PrepOptions _options = null!;
    private AdventureService _adventureService = null!;
    private AdventureDraftingService _sut = null!;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _adventures = new InMemoryAdventureRepository();
        _ledger = new InMemoryAiUsageRecordRepository();
        _artifacts = new InMemoryArtifactRepository();
        _facts = new InMemoryArtifactFactRepository();
        _ai = new FakePrepAiClient();
        _budget = new FakeAiBudgetGuard();
        _gate = new SectionDraftGate(TimeSpan.FromMinutes(5));
        _gauge = new StubConvergenceGauge();
        _options = new PrepOptions();

        var loremaster = new LoremasterOptions { AiModel = "nornis-ask", MaxRetrievalCount = 30, MaxFactsPerArtifact = 15 };
        _retriever = new SpyKnowledgeRetriever(new KeywordKnowledgeRetriever(
            _artifacts, _facts, new InMemoryArtifactRelationshipRepository(), new InMemorySourceReferenceRepository(),
            new InMemorySourceRepository(), Options.Create(loremaster)));

        var worlds = new InMemoryWorldRepository();
        await worlds.CreateAsync(new World
        {
            Id = _worldId,
            Name = "Black Harbor",
            GameSystem = "Fate Accelerated",
            CreatedByUserId = GmUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        _adventureService = new AdventureService(_adventures, new InMemoryCampaignRepository(),
            new FakeSlugResolver((_, _) => null), TestPrintDetails.For(_worldId, GmUserId, worlds: worlds), Options.Create(_options));
        _sut = new AdventureDraftingService(
            _adventures, worlds, _retriever, _ai, _budget, TestUsageRecorder.Wrap(_ledger), _gate, _gauge,
            Options.Create(_options), Options.Create(loremaster), NullLogger<AdventureDraftingService>.Instance);
    }

    // ------------------------------------------------------------------ helpers --

    private async Task<Adventure> CreateAdventureAsync(string? pitch = "Captain Voss hides a silver key in the harbor.")
    {
        var created = await _adventureService.CreateAsync(
            new CreateAdventureCommand(_worldId, GmUserId, WorldRole.GM, "The Silver Key", pitch, null), CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
        return created.Value!.Adventure;
    }

    private AdventureSection Stored(Guid sectionId) => _adventures.Sections.Single(s => s.Id == sectionId);

    private static AdventureSection Scene(Adventure adventure) => adventure.Sections.First(s => s.Kind == AdventureSectionKind.Scene);

    private DraftSectionCommand Command(
        Adventure adventure, AdventureSection section, string? instruction = null,
        WorldRole role = WorldRole.GM, DateTimeOffset? basedOn = null) =>
        new(_worldId, adventure.Id, section.Id, GmUserId, role, basedOn ?? Stored(section.Id).UpdatedAt, instruction);

    private static async Task<List<DraftEvent>> DrainAsync(IAsyncEnumerable<DraftEvent> events)
    {
        var list = new List<DraftEvent>();
        await foreach (var e in events)
        {
            list.Add(e);
        }

        return list;
    }

    private async Task<List<DraftEvent>> DraftAsync(DraftSectionCommand command)
    {
        var result = await _sut.DraftAsync(command, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return await DrainAsync(result.Value!);
    }

    // ---------------------------------------------------------- before the stream --

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task ANonGm_IsRefused_BeforeAnythingIsRetrievedOrSpent(WorldRole role)
    {
        var adventure = await CreateAdventureAsync();

        var result = await _sut.DraftAsync(Command(adventure, Scene(adventure), role: role), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.StatusCode, Is.EqualTo(403));
            Assert.That(_retriever.Calls, Is.Empty);
            Assert.That(_budget.PrepChecks, Is.Zero);
            Assert.That(_ai.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task AStaleBasedOn_IsA409_AndNothingIsCalled()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);

        var result = await _sut.DraftAsync(Command(adventure, scene, basedOn: scene.UpdatedAt.AddSeconds(-1)), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("section_changed"));
        Assert.That(_ai.Requests, Is.Empty);
    }

    [Test]
    public async Task AHeldGate_IsA409()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        using var held = _gate.TryEnter(scene.Id);

        var result = await _sut.DraftAsync(Command(adventure, scene), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error?.Code, Is.EqualTo("draft_in_progress"));
    }

    [Test]
    public async Task TheGate_IsReleased_WhenTheStreamEnds()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        _ai.Script("Gulls wheel over the docks.");

        await DraftAsync(Command(adventure, scene));

        using var again = _gate.TryEnter(scene.Id);
        Assert.That(again, Is.Not.Null);
    }

    [Test]
    public async Task ASpentPrepBudget_Refuses_AndTheDailyCheckNeverRuns()
    {
        var adventure = await CreateAdventureAsync();
        _budget.PrepExceeded = true;

        var result = await _sut.DraftAsync(Command(adventure, Scene(adventure)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo("prep_budget_exceeded"));
            Assert.That(_ai.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task AnOverInstruction_IsRefused()
    {
        var adventure = await CreateAdventureAsync();

        var result = await _sut.DraftAsync(
            Command(adventure, Scene(adventure), new string('x', _options.MaxInstructionChars + 1)), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task AnEmptyOverview_WithNoPitchAndNoInstruction_AsksForOne()
    {
        var adventure = await CreateAdventureAsync(pitch: null);
        var overview = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Overview);

        var refused = await _sut.DraftAsync(Command(adventure, overview), CancellationToken.None);
        _ai.Script("A heist in a drowned city.");
        var allowed = await _sut.DraftAsync(Command(adventure, overview, "A heist in a drowned city."), CancellationToken.None);

        Assert.That(refused.Error?.Code, Is.EqualTo("instruction_required"));
        Assert.That(allowed.IsSuccess, Is.True);
    }

    [Test]
    public async Task AnotherWorldsAdventure_IsNotFound()
    {
        var adventure = await CreateAdventureAsync();
        var command = Command(adventure, Scene(adventure)) with { WorldId = Guid.NewGuid() };

        var result = await _sut.DraftAsync(command, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
    }

    // --------------------------------------------------------------- completion --

    [Test]
    public async Task Completion_StreamsTheText_WritesTheBody_AndOneRevisionOfTheOldBody()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        var before = Stored(scene.Id).BodyMarkdown;
        _ai.Script("> Gulls wheel ", "over the docks.");

        var events = await DraftAsync(Command(adventure, scene, "Open on the docks at dawn."));

        var completed = events.OfType<DraftEvent.Completed>().Single();
        var revision = _adventures.Revisions.Single(r => r.SectionId == scene.Id);
        Assert.Multiple(() =>
        {
            Assert.That(events.OfType<DraftEvent.Delta>().Select(d => d.Text), Is.EqualTo(["> Gulls wheel ", "over the docks."]));
            Assert.That(events[^1], Is.SameAs(completed));
            Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo("> Gulls wheel over the docks."));
            Assert.That(Stored(scene.Id).UpdatedAt, Is.GreaterThan(scene.UpdatedAt));
            Assert.That(completed.Section.UpdatedAt, Is.EqualTo(Stored(scene.Id).UpdatedAt));
            Assert.That(completed.RevisionCount, Is.EqualTo(1));
            Assert.That(revision.BodyMarkdown, Is.EqualTo(before));
            Assert.That(revision.Instruction, Is.EqualTo("Open on the docks at dawn."));
            Assert.That(revision.Model, Is.EqualTo("nornis-ask"));
        });
    }

    [Test]
    public async Task Completion_RecordsOneLedgerRow_UnderAdventureDrafting_WithCachedTokens()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Script("Gulls wheel over the docks.");
        _ai.Usage = new AiUsage
        {
            Model = "nornis-ask",
            InputTokens = 4_000,
            CachedInputTokens = 3_072,
            OutputTokens = 600,
            TotalTokens = 4_600,
            DurationMs = 0,
        };

        await DraftAsync(Command(adventure, Scene(adventure)));

        var row = _ledger.Records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.OperationType, Is.EqualTo(AiOperationType.AdventureDrafting));
            Assert.That(row.WorldId, Is.EqualTo(_worldId));
            Assert.That(row.UserId, Is.EqualTo(GmUserId));
            Assert.That(row.CachedInputTokens, Is.EqualTo(3_072));
            Assert.That(row.Succeeded, Is.True);
            Assert.That(row.ErrorCode, Is.Null);
        });
    }

    [Test]
    public async Task TheRequest_UsesTheDraftPrompt_AndTheDraftTimeout()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Script("Gulls.");

        await DraftAsync(Command(adventure, Scene(adventure)));

        var request = _ai.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.SystemPrompt, Is.EqualTo(AdventureDraftPrompt.SystemPrompt));
            Assert.That(request.UserMessage, Does.Contain("Game system: Fate Accelerated"));
            Assert.That(request.UserMessage, Does.Contain("## The section to write: [Scene] Scene 1"));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(_options.DraftTimeoutSeconds));
        });
    }

    [Test]
    public async Task PrepDeployment_IsTheModelTheRevisionAndRequestName()
    {
        _options.Deployment = "nornis-prep";
        var adventure = await CreateAdventureAsync();
        _ai.Script("Gulls.");

        await DraftAsync(Command(adventure, Scene(adventure)));

        Assert.That(_ai.Requests.Single().Model, Is.EqualTo("nornis-prep"));
        Assert.That(_adventures.Revisions.Single().Model, Is.EqualTo("nornis-prep"));
    }

    [Test]
    public async Task TheTwentyFirstRevision_TrimsTheOldest()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        for (var i = 0; i < _options.MaxRevisionsPerSection; i++)
        {
            _adventures.SeedRevision(new AdventureSectionRevision
            {
                Id = Guid.NewGuid(),
                SectionId = scene.Id,
                BodyMarkdown = $"old {i}",
                Model = "nornis-ask",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-100 + i),
            });
        }

        _ai.Script("Gulls.");
        var events = await DraftAsync(Command(adventure, scene));

        var kept = _adventures.Revisions.Where(r => r.SectionId == scene.Id).Select(r => r.BodyMarkdown).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(kept, Has.Count.EqualTo(_options.MaxRevisionsPerSection));
            Assert.That(kept, Does.Not.Contain("old 0"), "the oldest goes");
            Assert.That(kept, Does.Contain("old 1"));
            Assert.That(events.OfType<DraftEvent.Completed>().Single().RevisionCount, Is.EqualTo(_options.MaxRevisionsPerSection));
        });
    }

    [Test]
    public async Task Undo_AfterADraft_RestoresTheBodyTheDraftReplaced()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        var before = Stored(scene.Id).BodyMarkdown;
        _ai.Script("Gulls wheel over the docks.");
        await DraftAsync(Command(adventure, scene));

        var undone = await _adventureService.UndoAsync(
            new UndoSectionCommand(_worldId, adventure.Id, scene.Id, WorldRole.GM), CancellationToken.None);

        Assert.That(undone.IsSuccess, Is.True, undone.Error?.Message);
        Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo(before));
        Assert.That(_adventures.Revisions.Where(r => r.SectionId == scene.Id), Is.Empty);
    }

    // -------------------------------------------------- ends that write nothing --

    [Test]
    public async Task Cancel_WritesNothing_ButRecordsAnEstimatedRow()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        var before = Stored(scene.Id).BodyMarkdown;
        _ai.Script("Gulls wheel over ", "the docks and ");
        _ai.HangAfterText = true;

        using var cts = new CancellationTokenSource();
        var result = await _sut.DraftAsync(Command(adventure, scene), cts.Token);
        var seen = new List<string>();
        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in result.Value!.WithCancellation(cts.Token))
            {
                if (e is DraftEvent.Delta d)
                {
                    seen.Add(d.Text);
                    if (seen.Count == 2)
                    {
                        await cts.CancelAsync();
                    }
                }
            }
        });

        var row = _ledger.Records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo(before));
            Assert.That(Stored(scene.Id).UpdatedAt, Is.EqualTo(scene.UpdatedAt));
            Assert.That(_adventures.Revisions, Is.Empty);
            Assert.That(row.ErrorCode, Is.EqualTo("Cancelled"));
            Assert.That(row.OperationType, Is.EqualTo(AiOperationType.AdventureDrafting));
            Assert.That(row.OutputTokens, Is.GreaterThan(0), "text was written, so the row is estimated, not zero");
        });

        using var again = _gate.TryEnter(scene.Id);
        Assert.That(again, Is.Not.Null, "a cancelled draft releases its section");
    }

    [Test]
    public async Task AFailureMidStream_WritesNothing_AndEndsInFailed()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        var before = Stored(scene.Id).BodyMarkdown;
        _ai.Script("Gulls wheel ");
        _ai.ThrowAfterText = new AiHttpException("boom", 500, null);

        var events = await DraftAsync(Command(adventure, scene));

        Assert.Multiple(() =>
        {
            Assert.That(events[^1], Is.InstanceOf<DraftEvent.Failed>());
            Assert.That(((DraftEvent.Failed)events[^1]).Error.StatusCode, Is.EqualTo(503));
            Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo(before));
            Assert.That(_adventures.Revisions, Is.Empty);
            Assert.That(_ledger.Records.Single().Succeeded, Is.False);
        });
    }

    [Test]
    public async Task ADraftCutOffAtTheCeiling_IsNotSaved()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        var before = Stored(scene.Id).BodyMarkdown;
        _ai.Script("Gulls wheel over the");
        _ai.CutOff = true;

        var events = await DraftAsync(Command(adventure, scene));

        Assert.Multiple(() =>
        {
            Assert.That(((DraftEvent.Failed)events[^1]).Error.Code, Is.EqualTo("draft_cut_off"));
            Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo(before));
            Assert.That(_adventures.Revisions, Is.Empty);
            Assert.That(_ledger.Records.Single().ErrorCode, Is.EqualTo("Refused"));
        });
    }

    [Test]
    public async Task AnEmptyDraft_IsNotSaved()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        _ai.Script("  \n ");

        var events = await DraftAsync(Command(adventure, scene));

        Assert.That(((DraftEvent.Failed)events[^1]).Error.Code, Is.EqualTo("draft_empty"));
        Assert.That(_adventures.Revisions, Is.Empty);
    }

    [Test]
    public async Task ADraftOverTheSectionBound_IsNotSaved()
    {
        _options.MaxSectionChars = 10;
        var adventure = await CreateAdventureAsync();
        var scene = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Chapter);
        _ai.Script("Gulls wheel over the docks.");

        var events = await DraftAsync(Command(adventure, scene));

        Assert.That(((DraftEvent.Failed)events[^1]).Error.Code, Is.EqualTo("prep_too_large"));
        Assert.That(_adventures.Revisions, Is.Empty);
    }

    [Test]
    public async Task AnEditWhileTheDraftStreams_Wins_AndTheDraftIsNotSaved()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        var command = Command(adventure, scene);
        _ai.Script("Gulls wheel over the docks.");
        _ai.BeforeEnd = async () =>
        {
            var saved = await _adventureService.SaveSectionAsync(new SaveSectionCommand(
                _worldId, adventure.Id, scene.Id, WorldRole.GM, scene.Kind, scene.Title, "The GM's own words.", scene.UpdatedAt),
                CancellationToken.None);
            Assert.That(saved.IsSuccess, Is.True);
        };

        var events = await DraftAsync(command);

        Assert.Multiple(() =>
        {
            Assert.That(((DraftEvent.Failed)events[^1]).Error.Code, Is.EqualTo("section_changed"));
            Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo("The GM's own words."));
            Assert.That(_adventures.Revisions, Is.Empty);
        });
    }

    // ----------------------------------------------------------- Hidden truth --

    [Test]
    public async Task TheGmsPrompt_HoldsAHiddenFact_ThatTheSameRetrievalAtPlayerRoleDoesNot()
    {
        var voss = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Summary = "Harbor captain.",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = [],
        };
        _artifacts.Seed(voss);
        _facts.Seed(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = voss.Id,
            Predicate = "true allegiance",
            Value = "The Shadow Guild",
            TruthState = TruthState.Hidden,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var adventure = await CreateAdventureAsync();
        _ai.Script("Voss waits on the quay.");

        await DraftAsync(Command(adventure, Scene(adventure)));

        var (text, role) = _retriever.Calls.Single();
        var asPlayer = await _retriever.Inner.RetrieveAsync(text, _worldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(role, Is.EqualTo(WorldRole.GM));
            Assert.That(_ai.Requests.Single().UserMessage, Does.Contain("The Shadow Guild"));
            Assert.That(LoremasterService.FormatKnowledgeContext(asPlayer), Does.Not.Contain("The Shadow Guild"));
            Assert.That(LoremasterService.FormatKnowledgeContext(asPlayer), Does.Contain("Captain Voss"),
                "the Player retrieval found Voss, so the fact's absence is the Hidden rule, not a miss");
        });
    }

    [Test]
    public async Task ARevision_RetrievesFromTheSectionsOwnText()
    {
        var adventure = await CreateAdventureAsync(pitch: "A heist.");
        var chapter = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Chapter);
        await _adventureService.SaveSectionAsync(new SaveSectionCommand(
            _worldId, adventure.Id, chapter.Id, WorldRole.GM, chapter.Kind, chapter.Title,
            "Mirela Thorn meets the crew at the lighthouse.", chapter.UpdatedAt), CancellationToken.None);
        _ai.Script("Tighter.");

        await DraftAsync(Command(adventure, Stored(chapter.Id)));

        Assert.That(_retriever.Calls.Single().Text, Does.Contain("Mirela Thorn"));
    }

    // ------------------------------------------------------- threads to use (3.1) --

    private static AdventureSection Overview(Adventure adventure) => adventure.Sections.First(s => s.Kind == AdventureSectionKind.Overview);

    [Test]
    public async Task AnOverviewDraft_IsGivenTheGaugesTopFive_ReadAtTheGmsRole()
    {
        var adventure = await CreateAdventureAsync();
        _gauge.Candidates = Enumerable.Range(1, 7)
            .Select(i => StubConvergenceGauge.Candidate($"Anchor {i}", $"Secret number {i}.", 100 - i))
            .ToList();
        _ai.Script("A heist in the fog.");

        await DraftAsync(Command(adventure, Overview(adventure)));

        var message = _ai.Requests.Single().UserMessage;
        Assert.Multiple(() =>
        {
            Assert.That(_gauge.Calls, Is.EqualTo([WorldRole.GM]));
            Assert.That(message, Does.Contain("### Threads ready to pay off"));
            Assert.That(message, Does.Contain("- Anchor 1: Secret number 1."));
            Assert.That(message, Does.Contain("- Anchor 5: Secret number 5."));
            Assert.That(message, Does.Not.Contain("Anchor 6"), "five, the gauge's strongest");
        });
    }

    [Test]
    public async Task ASceneDraft_NeverAsksTheGauge_AndHasNoThreads()
    {
        var adventure = await CreateAdventureAsync();
        _gauge.Candidates = [StubConvergenceGauge.Candidate("Captain Voss", "Voss is the smuggler king.", 90)];
        _ai.Script("Gulls.");

        await DraftAsync(Command(adventure, Scene(adventure)));

        Assert.Multiple(() =>
        {
            Assert.That(_gauge.Calls, Is.Empty);
            Assert.That(_ai.Requests.Single().UserMessage, Does.Not.Contain("Threads ready to pay off"));
        });
    }

    [Test]
    public async Task AnEmptyGauge_LeavesTheBlockOut()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Script("A heist.");

        await DraftAsync(Command(adventure, Overview(adventure)));

        Assert.Multiple(() =>
        {
            Assert.That(_gauge.Calls, Has.Count.EqualTo(1));
            Assert.That(_ai.Requests.Single().UserMessage, Does.Not.Contain("Threads ready to pay off"));
        });
    }

    [Test]
    public async Task AGaugeThatRefuses_StillDrafts_WithoutThreads()
    {
        var adventure = await CreateAdventureAsync();
        _gauge.Error = new AppError(403, "forbidden", "GM only.");
        _ai.Script("A heist.");

        var events = await DraftAsync(Command(adventure, Overview(adventure)));

        Assert.Multiple(() =>
        {
            Assert.That(events.OfType<DraftEvent.Completed>(), Has.Exactly(1).Items);
            Assert.That(_ai.Requests.Single().UserMessage, Does.Not.Contain("Threads ready to pay off"));
        });
    }

    [Test]
    public async Task AGaugeThatThrows_StillDrafts_WithoutThreads()
    {
        var adventure = await CreateAdventureAsync();
        _gauge.Throw = new InvalidOperationException("the gauge broke");
        _ai.Script("A heist.");

        var events = await DraftAsync(Command(adventure, Overview(adventure)));

        Assert.Multiple(() =>
        {
            Assert.That(events.OfType<DraftEvent.Completed>(), Has.Exactly(1).Items);
            Assert.That(_ai.Requests.Single().UserMessage, Does.Not.Contain("Threads ready to pay off"));
        });
    }
}
