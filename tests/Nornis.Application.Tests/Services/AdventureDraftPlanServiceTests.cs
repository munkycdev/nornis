using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
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
/// The split plan (feature 37): the headings plan uses no AI; the Loremaster's carries the brief,
/// and one that can't be used falls back to the headings with a note. Nothing is ever written.
/// </summary>
[TestFixture]
public class AdventureDraftPlanServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();
    private const string Draft = "Steam everywhere.\n\n## The Baths\n\nSlimes lurk in the warm pools, waiting for bathers.\n\n## The Boiler\n\nIt hisses.";

    private InMemoryAdventureRepository _adventures = null!;
    private FakePrepAiClient _ai = null!;
    private FakeAiBudgetGuard _budget = null!;
    private InMemoryAiUsageRecordRepository _ledger = null!;
    private AdventureDraftPlanService _sut = null!;
    private Guid _adventureId;

    [SetUp]
    public async Task SetUp()
    {
        _adventures = new InMemoryAdventureRepository();
        _ai = new FakePrepAiClient();
        _budget = new FakeAiBudgetGuard();
        _ledger = new InMemoryAiUsageRecordRepository();
        var loremaster = new LoremasterOptions { AiModel = "nornis-ask", MaxRetrievalCount = 30, MaxFactsPerArtifact = 15 };
        var worlds = new InMemoryWorldRepository();
        await worlds.CreateAsync(new World
        {
            Id = WorldId,
            Name = "Slime Coast",
            GameSystem = "Shadowdark",
            CreatedByUserId = GmUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var prompts = new PrepPromptContextBuilder(worlds, new KeywordKnowledgeRetriever(
            new InMemoryArtifactRepository(), new InMemoryArtifactFactRepository(), new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceReferenceRepository(), new InMemorySourceRepository(), Options.Create(loremaster)), new FakeReferencePassageRetriever(), Options.Create(loremaster));
        _sut = new AdventureDraftPlanService(_adventures, prompts, _ai, _budget, TestUsageRecorder.Wrap(_ledger),
            Options.Create(new PrepOptions()), Options.Create(loremaster), NullLogger<AdventureDraftPlanService>.Instance);

        var views = new AdventureService(_adventures, new InMemoryCampaignRepository(), new FakeSlugResolver((_, _) => null),
            TestBooklets.Printer(TestPrintDetails.For(WorldId, GmUserId, "Shadowdark"), new FakeBookletPdfRenderer()),
            Options.Create(new PrepOptions()), new FakeBlobStorageService(), new FakeAiBudgetGuard());
        _adventureId = (await views.CreateAsync(new CreateAdventureCommand(WorldId, GmUserId, WorldRole.GM, "The Slurping Spa", null, null), default))
            .Value!.Adventure.Id;
        var adventure = _adventures.Adventures.Single();
        adventure.DraftMarkdown = Draft;
        adventure.DraftUpdatedAt = DateTimeOffset.UtcNow;
        adventure.TableBrief = "Four players, two hours.";
    }

    private Task<Nornis.Application.Errors.AppResult<DraftPlanView>> PlanAsync(bool loremaster, WorldRole role = WorldRole.GM) =>
        _sut.PlanAsync(new PlanDraftCommand(WorldId, _adventureId, GmUserId, role, loremaster), default);

    [Test]
    public async Task TheHeadingsPlan_UsesNoAi_AndShowsEachSectionsOpeningWords()
    {
        var plan = (await PlanAsync(loremaster: false)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(_ai.Requests, Is.Empty);
            Assert.That(plan.Source, Is.EqualTo(DraftPlanSource.Headings));
            Assert.That(plan.Rows.Select(r => r.Title), Is.EqualTo(["Overview", "The Baths", "The Boiler"]));
            Assert.That(plan.Rows[1].Opening, Is.EqualTo("Slimes lurk in the warm pools, waiting for bathers."));
            Assert.That(plan.Note, Is.Null);
        });
    }

    [Test]
    public async Task ADraftWithNoHeadings_SaysSo()
    {
        _adventures.Adventures.Single().DraftMarkdown = "Just one long text.";

        var plan = (await PlanAsync(loremaster: false)).Value!;

        Assert.That(plan.Note, Does.Contain("no # or ## headings"));
    }

    [Test]
    public async Task TheLoremastersPlan_IsUsed_WhenItCanBeCut_AndThePromptCarriesTheBrief()
    {
        _ai.Script("""[{"startLine": 1, "title": "Overview", "kind": "Overview"}, {"startLine": 3, "title": "The Baths", "kind": "Scene"}]""");

        var plan = (await PlanAsync(loremaster: true)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Source, Is.EqualTo(DraftPlanSource.Loremaster));
            Assert.That(plan.Rows.Select(r => r.StartLine), Is.EqualTo([1, 3]));
            Assert.That(_ai.Requests.Single().UserMessage, Does.Contain("Four players, two hours."));
            Assert.That(_ai.Requests.Single().UserMessage, Does.Contain("3| ## The Baths"));
            Assert.That(_ledger.Records, Has.Count.EqualTo(1), "the call is paid for from the Prep pool");
        });
    }

    [TestCase("""[{"startLine": 1, "title": "A", "kind": "Scene"}, {"startLine": 99, "title": "B", "kind": "Scene"}]""", TestName = "ALinePastTheEnd")]
    [TestCase("""[{"startLine": 5, "title": "A", "kind": "Scene"}, {"startLine": 3, "title": "B", "kind": "Scene"}]""", TestName = "OutOfOrder")]
    [TestCase("I'd split it at the baths.", TestName = "NotJson")]
    public async Task APlanThatCantBeUsed_FallsBackToTheHeadings_WithANote(string answer)
    {
        _ai.Script(answer);

        var plan = (await PlanAsync(loremaster: true)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Source, Is.EqualTo(DraftPlanSource.Headings));
            Assert.That(plan.Rows.Select(r => r.Title), Is.EqualTo(["Overview", "The Baths", "The Boiler"]));
            Assert.That(plan.Note, Does.Contain("couldn't be used"));
        });
    }

    [Test]
    public async Task AFailedCall_FallsBackToTheHeadings()
    {
        _ai.Script("[");
        _ai.ThrowAfterText = new AiTimeoutException("slow");

        var plan = (await PlanAsync(loremaster: true)).Value!;

        Assert.That(plan.Note, Does.Contain("couldn't plan the split just now"));
    }

    [Test]
    public async Task APlan_IsGmOnly_AndAnEmptyDraftHasNone()
    {
        var player = await PlanAsync(loremaster: false, WorldRole.Player);
        _adventures.Adventures.Single().DraftMarkdown = "  ";
        var empty = await PlanAsync(loremaster: false);

        Assert.That(new[] { player.Error?.StatusCode, empty.Error?.StatusCode }, Is.EqualTo(new int?[] { 403, 400 }));
    }
}
