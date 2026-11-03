using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
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
/// An Adventure's side conversation (feature 30, design "A turn" and "Put in section"): what
/// refuses a turn and in what order, that the GM's message is saved before the call and an
/// answer only on a clean end, the history window, Hidden truth at GM role, the ledger row,
/// Clear, and Put in section writing through the revision path so Undo gives the text back.
/// </summary>
[TestFixture]
public class AdventureConversationServiceTests
{
    private static readonly Guid GmUserId = Guid.NewGuid();

    private Guid _worldId;
    private InMemoryAdventureRepository _adventures = null!;
    private InMemoryConversationRepository _conversations = null!;
    private InMemoryAiUsageRecordRepository _ledger = null!;
    private InMemoryArtifactRepository _artifacts = null!;
    private InMemoryArtifactFactRepository _facts = null!;
    private SpyKnowledgeRetriever _retriever = null!;
    private FakePrepAiClient _ai = null!;
    private FakeAiBudgetGuard _budget = null!;
    private SectionDraftGate _gate = null!;
    private PrepOptions _options = null!;
    private AdventureService _adventureService = null!;
    private AdventureConversationService _sut = null!;

    private FakeReferencePassageRetriever _references = null!;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _adventures = new InMemoryAdventureRepository();
        _conversations = new InMemoryConversationRepository();
        _ledger = new InMemoryAiUsageRecordRepository();
        _artifacts = new InMemoryArtifactRepository();
        _facts = new InMemoryArtifactFactRepository();
        _ai = new FakePrepAiClient();
        _budget = new FakeAiBudgetGuard();
        _gate = new SectionDraftGate(TimeSpan.FromMinutes(5));
        _options = new PrepOptions();

        var loremaster = new LoremasterOptions { AiModel = "nornis-ask", MaxRetrievalCount = 30, MaxFactsPerArtifact = 15 };
        _retriever = new SpyKnowledgeRetriever(new KeywordKnowledgeRetriever(
            _artifacts, _facts, new InMemoryArtifactRelationshipRepository(), new InMemorySourceReferenceRepository(),
            new InMemorySourceRepository(), Options.Create(loremaster)));

        _references = new FakeReferencePassageRetriever();
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
            new FakeSlugResolver((_, _) => null), TestBooklets.Printer(TestPrintDetails.For(_worldId, GmUserId, worlds: worlds)), Options.Create(_options));
        _sut = new AdventureConversationService(
            _adventures, _conversations, new PrepPromptContextBuilder(worlds, _retriever, _references, Options.Create(loremaster)), _ai, _budget, TestUsageRecorder.Wrap(_ledger), _gate,
            Options.Create(_options), Options.Create(loremaster), NullLogger<AdventureConversationService>.Instance);
    }

    // ------------------------------------------------------------------ helpers --

    private async Task<Adventure> CreateAdventureAsync(string title = "The Silver Key")
    {
        var created = await _adventureService.CreateAsync(
            new CreateAdventureCommand(_worldId, GmUserId, WorldRole.GM, title, "Captain Voss hides a silver key.", null),
            CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
        return created.Value!.Adventure;
    }

    private AdventureSection Stored(Guid sectionId) => _adventures.Sections.Single(s => s.Id == sectionId);

    private static AdventureSection Scene(Adventure adventure) => adventure.Sections.First(s => s.Kind == AdventureSectionKind.Scene);

    private SendConversationMessageCommand Send(
        Adventure adventure, string text = "Who else could the cult have recruited?", Guid? sectionId = null,
        WorldRole role = WorldRole.GM) =>
        new(_worldId, adventure.Id, GmUserId, role, text, sectionId);

    private async Task<List<ConversationEvent>> TurnAsync(SendConversationMessageCommand command)
    {
        var result = await _sut.SendAsync(command, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var list = new List<ConversationEvent>();
        await foreach (var e in result.Value!)
        {
            list.Add(e);
        }

        return list;
    }

    /// <summary>One full turn with the answer given, returning the saved answer.</summary>
    private async Task<ConversationMessage> AnsweredAsync(Adventure adventure, string answer, string question = "Give me a scene.")
    {
        _ai.Script(answer);
        var events = await TurnAsync(Send(adventure, question));
        return ((ConversationEvent.Completed)events[^1]).Answer;
    }

    private async Task SaveSectionAsync(Adventure adventure, AdventureSection section, string body)
    {
        var stored = Stored(section.Id);
        var saved = await _adventureService.SaveSectionAsync(new SaveSectionCommand(
            _worldId, adventure.Id, section.Id, WorldRole.GM, stored.Kind, stored.Title, body, stored.UpdatedAt), CancellationToken.None);
        Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
    }

    private PutInSectionCommand Put(
        Adventure adventure, AdventureSection section, ConversationMessage message, PutInSectionMode mode,
        DateTimeOffset? basedOn = null, WorldRole role = WorldRole.GM) =>
        new(_worldId, adventure.Id, section.Id, role, message.Id, mode, basedOn ?? Stored(section.Id).UpdatedAt);

    // ---------------------------------------------------------- before the stream --

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task ANonGm_IsRefusedEverything_BeforeAnythingIsReadOrSpent(WorldRole role)
    {
        var adventure = await CreateAdventureAsync();
        var answer = await AnsweredAsync(adventure, "Three cultists.");
        _retriever.Calls.Clear();
        _ai.Requests.Clear();
        var checks = _budget.PrepChecks;

        var get = await _sut.GetAsync(_worldId, adventure.Id, role, CancellationToken.None);
        var send = await _sut.SendAsync(Send(adventure, role: role), CancellationToken.None);
        var clear = await _sut.ClearAsync(_worldId, adventure.Id, role, CancellationToken.None);
        var put = await _sut.PutInSectionAsync(Put(adventure, Scene(adventure), answer, PutInSectionMode.Replace, role: role),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(get.Error?.StatusCode, Is.EqualTo(403));
            Assert.That(send.Error?.StatusCode, Is.EqualTo(403));
            Assert.That(clear.Error?.StatusCode, Is.EqualTo(403));
            Assert.That(put.Error?.StatusCode, Is.EqualTo(403));
            Assert.That(_retriever.Calls, Is.Empty);
            Assert.That(_budget.PrepChecks, Is.EqualTo(checks));
            Assert.That(_ai.Requests, Is.Empty);
            Assert.That(_conversations.Messages, Has.Count.EqualTo(2), "nothing cleared, nothing added");
        });
    }

    [Test]
    public async Task TheRefusalOrder_IsRole_ThenMessage_ThenAdventure()
    {
        var missing = new Adventure { Id = Guid.NewGuid(), WorldId = _worldId };

        var asPlayer = await _sut.SendAsync(Send(missing, "", role: WorldRole.Player), CancellationToken.None);
        var blank = await _sut.SendAsync(Send(missing, "   "), CancellationToken.None);
        var notFound = await _sut.SendAsync(Send(missing, "Hello"), CancellationToken.None);

        Assert.That(asPlayer.Error?.StatusCode, Is.EqualTo(403));
        Assert.That(blank.Error?.StatusCode, Is.EqualTo(400));
        Assert.That(notFound.Error?.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task AnOverlongMessage_IsRefused_WithTheSizeAndTheBound()
    {
        var adventure = await CreateAdventureAsync();

        var result = await _sut.SendAsync(Send(adventure, new string('x', _options.MaxConversationMessageChars + 1)), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_too_large"));
        Assert.That(result.Error?.Message, Does.Contain("4,001").And.Contain("4,000"));
        Assert.That(_conversations.Messages, Is.Empty);
    }

    [Test]
    public async Task AnAdventureInAnotherWorld_OrASectionNotInIt_Is404()
    {
        var adventure = await CreateAdventureAsync();

        var otherWorld = await _sut.SendAsync(Send(adventure) with { WorldId = Guid.NewGuid() }, CancellationToken.None);
        var strangeSection = await _sut.SendAsync(Send(adventure, sectionId: Guid.NewGuid()), CancellationToken.None);

        Assert.That(otherWorld.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(strangeSection.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(_conversations.Messages, Is.Empty);
    }

    [Test]
    public async Task AFullConversation_RefusesTheNextTurn_AndSaysItIsFull()
    {
        _options.MaxConversationMessages = 3;
        var adventure = await CreateAdventureAsync();
        await AnsweredAsync(adventure, "One.");

        var result = await _sut.SendAsync(Send(adventure), CancellationToken.None);
        var view = await _sut.GetAsync(_worldId, adventure.Id, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("conversation_full"));
        Assert.That(view.Value!.IsFull, Is.True);
        Assert.That(_conversations.Messages, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ASpentPrepBudget_Refuses_AndSavesNothing()
    {
        var adventure = await CreateAdventureAsync();
        _budget.PrepExceeded = true;

        var result = await _sut.SendAsync(Send(adventure), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_budget_exceeded"));
        Assert.That(_conversations.Messages, Is.Empty);
        Assert.That(_ai.Requests, Is.Empty);
    }

    [Test]
    public async Task ATurnAlreadyRunning_IsA409_AndSavesNothing()
    {
        var adventure = await CreateAdventureAsync();
        using var held = _gate.TryEnter(adventure.Id);

        var result = await _sut.SendAsync(Send(adventure), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("answer_in_progress"));
        Assert.That(_conversations.Messages, Is.Empty);
    }

    [Test]
    public async Task ADraftOfASection_DoesNotBlockATurn()
    {
        var adventure = await CreateAdventureAsync();
        using var draft = _gate.TryEnter(Scene(adventure).Id);
        _ai.Script("Yes.");

        var events = await TurnAsync(Send(adventure, sectionId: Scene(adventure).Id));

        Assert.That(events[^1], Is.InstanceOf<ConversationEvent.Completed>());
    }

    // -------------------------------------------------------------- the stream --

    [Test]
    public async Task TheGmsMessage_IsSavedBeforeTheModelIsCalled()
    {
        var adventure = await CreateAdventureAsync();
        int? savedWhenCalled = null;
        _ai.Script("Three cultists.");
        _ai.BeforeEnd = () =>
        {
            savedWhenCalled = _conversations.Messages.Count(m => m.Role == ConversationRole.User);
            return Task.CompletedTask;
        };

        await TurnAsync(Send(adventure));

        Assert.That(savedWhenCalled, Is.EqualTo(1));
    }

    [Test]
    public async Task ACleanAnswer_StreamsThenIsSaved_UnderItsModel()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Script("Three ", "cultists.");

        var events = await TurnAsync(Send(adventure));

        var conversation = _conversations.Conversations.Single();
        Assert.Multiple(() =>
        {
            Assert.That(events.OfType<ConversationEvent.Delta>().Select(d => d.Text), Is.EqualTo(["Three ", "cultists."]));
            Assert.That(((ConversationEvent.Completed)events[^1]).Answer.Content, Is.EqualTo("Three cultists."));
            Assert.That(_conversations.Messages.Select(m => (m.Role, m.Content)), Is.EqualTo(
            [
                (ConversationRole.User, "Who else could the cult have recruited?"),
                (ConversationRole.Assistant, "Three cultists."),
            ]));
            Assert.That(_conversations.Messages[1].Model, Is.EqualTo("nornis-ask"));
            Assert.That(conversation.AdventureId, Is.EqualTo(adventure.Id));
            Assert.That(conversation.UserId, Is.EqualTo(GmUserId));
            Assert.That(conversation.Title, Is.EqualTo("The Silver Key"));
        });
    }

    [Test]
    public async Task ASecondTurn_GoesIntoTheSameConversation()
    {
        var adventure = await CreateAdventureAsync();
        await AnsweredAsync(adventure, "One.");
        await AnsweredAsync(adventure, "Two.");

        Assert.That(_conversations.Conversations, Has.Count.EqualTo(1));
        Assert.That(_conversations.Messages, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task AnAnswerCutOffAtTheCeiling_IsNotSaved_ButTheQuestionIs()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Script("Three cultists, and");
        _ai.CutOff = true;

        var events = await TurnAsync(Send(adventure));

        Assert.Multiple(() =>
        {
            Assert.That(((ConversationEvent.Failed)events[^1]).Error.Code, Is.EqualTo("answer_cut_off"));
            Assert.That(_conversations.Messages.Single().Role, Is.EqualTo(ConversationRole.User));
            Assert.That(_ledger.Records.Single().ErrorCode, Is.EqualTo("Refused"));
        });
    }

    [Test]
    public async Task AnEmptyAnswer_IsNotSaved()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Script("  \n ");

        var events = await TurnAsync(Send(adventure));

        Assert.That(((ConversationEvent.Failed)events[^1]).Error.Code, Is.EqualTo("answer_empty"));
        Assert.That(_conversations.Messages.Single().Role, Is.EqualTo(ConversationRole.User));
    }

    [Test]
    public async Task AFailureMidStream_SavesNoAnswer_AndEndsInFailed()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Script("Three ");
        _ai.ThrowAfterText = new AiHttpException("boom", 500, null);

        var events = await TurnAsync(Send(adventure));

        Assert.Multiple(() =>
        {
            Assert.That(((ConversationEvent.Failed)events[^1]).Error.StatusCode, Is.EqualTo(503));
            Assert.That(_conversations.Messages.Single().Role, Is.EqualTo(ConversationRole.User));
            Assert.That(_ledger.Records.Single().Succeeded, Is.False);
        });
    }

    [Test]
    public async Task AStoppedAnswer_IsNotSaved_ButIsPaidFor()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Script("Three cultists ");
        _ai.HangAfterText = true;
        using var cts = new CancellationTokenSource();

        var result = await _sut.SendAsync(Send(adventure), cts.Token);
        Assert.That(async () =>
        {
            await foreach (var e in result.Value!.WithCancellation(cts.Token))
            {
                if (e is ConversationEvent.Delta)
                {
                    await cts.CancelAsync();
                }
            }
        }, Throws.InstanceOf<OperationCanceledException>());

        Assert.Multiple(() =>
        {
            Assert.That(_conversations.Messages.Single().Role, Is.EqualTo(ConversationRole.User));
            Assert.That(_ledger.Records.Single().ErrorCode, Is.EqualTo("Cancelled"));
            Assert.That(_ledger.Records.Single().OutputTokens, Is.GreaterThan(0), "estimated from what streamed");
        });
    }

    [Test]
    public async Task EveryTurn_IsRecordedAsAnAdventureConversation_InThePrepPool()
    {
        var adventure = await CreateAdventureAsync();
        _ai.Usage = new AiUsage { Model = "nornis-ask", InputTokens = 900, OutputTokens = 120, TotalTokens = 1020, DurationMs = 0 };

        await AnsweredAsync(adventure, "Three cultists.");

        var record = _ledger.Records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(record.OperationType, Is.EqualTo(AiOperationType.AdventureConversation));
            Assert.That(AiSpendPools.PoolOf(record.OperationType), Is.EqualTo(AiSpendPool.PrepMonthly));
            Assert.That(record.Succeeded, Is.True);
            Assert.That(record.InputTokens, Is.EqualTo(900));
        });
    }

    [Test]
    public async Task ATurn_CarriesTheDraftingCeilingsTimeout()
    {
        var adventure = await CreateAdventureAsync();

        await AnsweredAsync(adventure, "Yes.");

        Assert.That(_ai.Requests.Single().TimeoutSeconds, Is.EqualTo(_options.DraftTimeoutSeconds));
        Assert.That(_ai.Requests.Single().SystemPrompt, Is.EqualTo(AdventureConversationPrompt.SystemPrompt));
    }

    [Test]
    public async Task TheGate_IsReleased_WhenTheStreamEnds()
    {
        var adventure = await CreateAdventureAsync();

        await AnsweredAsync(adventure, "Yes.");

        using var again = _gate.TryEnter(adventure.Id);
        Assert.That(again, Is.Not.Null);
    }

    // ---------------------------------------------------------------- the prompt --

    [Test]
    public async Task TheHistory_SentWithATurn_IsTheNewestWholeMessagesThatFit()
    {
        var adventure = await CreateAdventureAsync();
        await AnsweredAsync(adventure, "OLDEST-ANSWER " + new string('a', 200), "OLDEST-QUESTION");
        await AnsweredAsync(adventure, "NEWER-ANSWER", "NEWER-QUESTION");
        _options.ConversationHistoryChars = 60;
        _ai.Requests.Clear();

        await AnsweredAsync(adventure, "Fine.", "THE-NEW-MESSAGE");

        var prompt = _ai.Requests.Single().UserMessage;
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("GM: NEWER-QUESTION"));
            Assert.That(prompt, Does.Contain("Loremaster: NEWER-ANSWER"));
            Assert.That(prompt, Does.Not.Contain("OLDEST-ANSWER"), "too long to fit: the window stops at it");
            Assert.That(prompt, Does.Not.Contain("OLDEST-QUESTION"), "older than a message that didn't fit");
            Assert.That(prompt, Does.Contain("THE-NEW-MESSAGE"));
        });
    }

    [Test]
    public async Task TheSectionOnScreen_RidesWithTheTurn()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        await SaveSectionAsync(adventure, scene, "Mirela Thorn waits at the lighthouse.");
        _ai.Script("Yes.");

        await TurnAsync(Send(adventure, sectionId: scene.Id));

        Assert.That(_ai.Requests.Single().UserMessage, Does.Contain("Mirela Thorn waits at the lighthouse."));
        Assert.That(_retriever.Calls.Single().Text, Does.Contain(scene.Title));
    }

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
        _ai.Script("He answers to the Guild.");

        await TurnAsync(Send(adventure, "What does Captain Voss want?"));

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

    // ------------------------------------------------------------------- Clear --

    [Test]
    public async Task Clear_DeletesTheMessages_AndLeavesTheAdventure()
    {
        var adventure = await CreateAdventureAsync();
        var answer = await AnsweredAsync(adventure, "Fog on the water.");
        var scene = Scene(adventure);
        await _sut.PutInSectionAsync(Put(adventure, scene, answer, PutInSectionMode.Replace), CancellationToken.None);

        var result = await _sut.ClearAsync(_worldId, adventure.Id, WorldRole.GM, CancellationToken.None);
        var view = await _sut.GetAsync(_worldId, adventure.Id, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(view.Value!.Messages, Is.Empty);
            Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo("Fog on the water."));
            Assert.That(_adventures.Revisions.Count(r => r.SectionId == scene.Id), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Clear_WhileAnAnswerIsWritten_IsA409()
    {
        var adventure = await CreateAdventureAsync();
        await AnsweredAsync(adventure, "One.");
        using var held = _gate.TryEnter(adventure.Id);

        var result = await _sut.ClearAsync(_worldId, adventure.Id, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("answer_in_progress"));
        Assert.That(_conversations.Messages, Has.Count.EqualTo(2));
    }

    // ------------------------------------------------------------ Put in section --

    [Test]
    public async Task Replace_PutsTheAnswerInTheSection_AndUndoGivesTheOldTextBack()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        await SaveSectionAsync(adventure, scene, "The GM's own words.");
        var answer = await AnsweredAsync(adventure, "> Fog rolls in off the water.");
        var calls = _ai.Requests.Count;
        Assert.That(_adventures.Adventures.Single(a => a.Id == adventure.Id).AiUse.WroteText, Is.False,
            "a conversation on its own records no AI use (feature 38)");

        var put = await _sut.PutInSectionAsync(Put(adventure, scene, answer, PutInSectionMode.Replace), CancellationToken.None);

        Assert.That(put.IsSuccess, Is.True, put.Error?.Message);
        Assert.That(put.Value!.Section.BodyMarkdown, Is.EqualTo("> Fog rolls in off the water."));
        Assert.That(put.Value.RevisionCount, Is.EqualTo(1));
        var revision = _adventures.Revisions.Single();
        Assert.That(revision.Instruction, Is.EqualTo(AdventureConversationService.PutInSectionInstruction));
        Assert.That(revision.Model, Is.EqualTo("nornis-ask"));
        Assert.That(_ai.Requests, Has.Count.EqualTo(calls), "Put in section makes no AI call");

        var undo = await _adventureService.UndoAsync(new UndoSectionCommand(_worldId, adventure.Id, scene.Id, WorldRole.GM),
            CancellationToken.None);
        Assert.That(undo.IsSuccess, Is.True);
        Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo("The GM's own words."));
        Assert.That(_adventures.Adventures.Single(a => a.Id == adventure.Id).AiUse.WroteText, Is.True,
            "Put in section records AI use, and Undo restores text, not the record");
    }

    [Test]
    public async Task Append_PutsTheAnswerAfterTheText_ABlankLineBetween()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        await SaveSectionAsync(adventure, scene, "The GM's own words.\n");
        var answer = await AnsweredAsync(adventure, "  More fog.  ");

        var put = await _sut.PutInSectionAsync(Put(adventure, scene, answer, PutInSectionMode.Append), CancellationToken.None);

        Assert.That(put.Value!.Section.BodyMarkdown, Is.EqualTo("The GM's own words.\n\nMore fog."));
    }

    [Test]
    public async Task Append_OntoAnUnwrittenSection_ReplacesTheTemplate()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        Assert.That(AdventureSkeleton.IsUnfilled(Stored(scene.Id).BodyMarkdown), Is.True);
        var answer = await AnsweredAsync(adventure, "More fog.");

        var put = await _sut.PutInSectionAsync(Put(adventure, scene, answer, PutInSectionMode.Append), CancellationToken.None);

        Assert.That(put.Value!.Section.BodyMarkdown, Is.EqualTo("More fog."));
    }

    [Test]
    public async Task AStaleStamp_IsA409_AndTheNewerEditStands()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        var answer = await AnsweredAsync(adventure, "More fog.");
        var opened = Stored(scene.Id).UpdatedAt;
        await SaveSectionAsync(adventure, scene, "A newer edit.");

        var put = await _sut.PutInSectionAsync(Put(adventure, scene, answer, PutInSectionMode.Replace, basedOn: opened),
            CancellationToken.None);

        Assert.That(put.Error?.Code, Is.EqualTo("section_changed"));
        Assert.That(Stored(scene.Id).BodyMarkdown, Is.EqualTo("A newer edit."));
        Assert.That(_adventures.Revisions, Is.Empty);
    }

    [Test]
    public async Task AnAnswerOverTheSectionBound_IsRefused_WithTheSizeAndTheBound()
    {
        var adventure = await CreateAdventureAsync();
        var scene = Scene(adventure);
        var answer = await AnsweredAsync(adventure, new string('x', 50));
        _options.MaxSectionChars = 20;

        var put = await _sut.PutInSectionAsync(Put(adventure, scene, answer, PutInSectionMode.Replace), CancellationToken.None);

        Assert.That(put.Error?.Code, Is.EqualTo("prep_too_large"));
        Assert.That(put.Error?.Message, Does.Contain("50").And.Contain("20"));
        Assert.That(_adventures.Revisions, Is.Empty);
    }

    [Test]
    public async Task OnlyAnAnswer_FromThisAdventuresConversation_CanBePutIn()
    {
        var adventure = await CreateAdventureAsync();
        var other = await CreateAdventureAsync("The Drowned Bell");
        await AnsweredAsync(adventure, "Mine.");
        var question = _conversations.Messages.First(m => m.Role == ConversationRole.User);
        var theirs = await AnsweredAsync(other, "Theirs.");

        var ownQuestion = await _sut.PutInSectionAsync(Put(adventure, Scene(adventure), question, PutInSectionMode.Replace),
            CancellationToken.None);
        var otherAnswer = await _sut.PutInSectionAsync(Put(adventure, Scene(adventure), theirs, PutInSectionMode.Replace),
            CancellationToken.None);

        Assert.That(ownQuestion.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(otherAnswer.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(_adventures.Revisions, Is.Empty);
    }

    [Test]
    public async Task Get_ReturnsTheConversationOldestFirst_AndEmptyBeforeTheFirstMessage()
    {
        var adventure = await CreateAdventureAsync();
        var before = await _sut.GetAsync(_worldId, adventure.Id, WorldRole.GM, CancellationToken.None);
        await AnsweredAsync(adventure, "One.", "First?");

        var after = await _sut.GetAsync(_worldId, adventure.Id, WorldRole.GM, CancellationToken.None);

        Assert.That(before.Value!.Messages, Is.Empty);
        Assert.That(after.Value!.Messages.Select(m => m.Content), Is.EqualTo(["First?", "One."]));
        Assert.That(after.Value.IsFull, Is.False);
    }

    // ------------------------------------------- the table and the books (feature 37) --

    private static KnowledgePassage Passage(Guid documentId, string title, string text) => new()
    {
        ChunkId = null,
        DocumentId = documentId,
        DocumentTitle = title,
        Page = 12,
        Text = text,
        ReferenceId = $"passage:{documentId:N}-11",
    };

    [Test]
    public async Task ThePrompt_CarriesTheTableBrief_AndAReferencePassage()
    {
        var adventure = await CreateAdventureAsync();
        _adventures.Adventures.Single(a => a.Id == adventure.Id).TableBrief = "Four players, level 3, two hours, one hates puzzles.";
        _references.Passages.Add(Passage(Guid.NewGuid(), "Slime Compendium", "Slimes dissolve copper on contact."));
        _ai.Script("A scene.");

        await TurnAsync(Send(adventure));

        var message = _ai.Requests.Single().UserMessage;
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("### The table\n\nFour players, level 3, two hours, one hates puzzles."));
            Assert.That(message, Does.Contain("From \"Slime Compendium\", p. 12"));
            Assert.That(message, Does.Contain("Slimes dissolve copper on contact."));
            Assert.That(_ai.Requests.Single().SystemPrompt, Does.Contain("cite them as (*Book title*, p. 12)"));
            Assert.That(_references.LastDocumentIds, Is.Null, "no source books chosen, so every book");
        });
    }

    [Test]
    public async Task InDesignMode_TheTurnSeesTheDraft_NotTheSectionOnScreen()
    {
        var adventure = await CreateAdventureAsync();
        _adventures.Adventures.Single(a => a.Id == adventure.Id).DraftMarkdown = "# The Spa\n\nTHE-WHOLE-DRAFT";
        _ai.Script("An edit.");

        await TurnAsync(Send(adventure, sectionId: adventure.Sections.First().Id) with { Scope = ConversationScope.Draft });

        var message = _ai.Requests.Single().UserMessage;
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("THE-WHOLE-DRAFT"));
            Assert.That(message, Does.Not.Contain("## The section the GM has open"));
            Assert.That(message, Does.Not.Contain("<- open on the GM's screen"));
        });
    }

    [Test]
    public async Task ChosenSourceBooks_AreTheOnlyBooksThePromptReads()
    {
        var adventure = await CreateAdventureAsync();
        var chosen = new LibraryDocument { Id = Guid.NewGuid(), WorldId = _worldId, Title = "Slime Compendium", Kind = LibraryDocumentKind.Sourcebook };
        _adventures.Documents.Add(chosen);
        await _adventures.ReplaceSourceBooksAsync(adventure.Id, [chosen.Id]);
        _references.Passages.Add(Passage(chosen.Id, chosen.Title, "Slimes dissolve copper on contact."));
        _references.Passages.Add(Passage(Guid.NewGuid(), "Dragon Manual", "Dragons hoard gold."));
        _ai.Script("A scene.");

        await TurnAsync(Send(adventure));

        var message = _ai.Requests.Single().UserMessage;
        Assert.Multiple(() =>
        {
            Assert.That(_references.LastDocumentIds, Is.EqualTo([chosen.Id]));
            Assert.That(message, Does.Contain("Slimes dissolve copper on contact."));
            Assert.That(message, Does.Not.Contain("Dragons hoard gold."));
        });
    }
}
