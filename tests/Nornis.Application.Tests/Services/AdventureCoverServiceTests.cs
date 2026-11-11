using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Art;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Application.Prep.Covers;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Cover art (feature 38): the brief reads only what a cover may show, generation is capped and
/// spent from Prep, choosing a generated option records AI art and an upload records nothing.
/// </summary>
[TestFixture]
public class AdventureCoverServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();
    private const string Brief = "A steaming bathhouse at dusk, green slime glistening on marble steps, lanterns in the fog.";

    private InMemoryAdventureRepository _adventures = null!;
    private FakePrepAiClient _ai = null!;
    private FakeImageArtGenerator _generator = null!;
    private FakeBlobStorageService _blobs = null!;
    private FakeAiBudgetGuard _budget = null!;
    private InMemoryAiUsageRecordRepository _ledger = null!;
    private PrepOptions _options = null!;
    private AdventureService _views = null!;
    private FakeBookletPdfRenderer _renderer = null!;
    private AdventureCoverService _sut = null!;
    private Guid _adventureId;

    [SetUp]
    public async Task SetUp()
    {
        _adventures = new InMemoryAdventureRepository();
        _ai = new FakePrepAiClient();
        _generator = new FakeImageArtGenerator();
        _blobs = new FakeBlobStorageService();
        _budget = new FakeAiBudgetGuard();
        _ledger = new InMemoryAiUsageRecordRepository();
        _options = new PrepOptions();
        var details = TestPrintDetails.For(WorldId, GmUserId, "Shadowdark");
        _sut = new AdventureCoverService(_adventures, details, _ai, _generator, _blobs, _budget, TestUsageRecorder.Wrap(_ledger),
            Options.Create(_options), Options.Create(new LoremasterOptions { AiModel = "nornis-ask" }), TimeProvider.System,
            NullLogger<AdventureCoverService>.Instance);
        _views = new AdventureService(_adventures, new InMemoryCampaignRepository(), new FakeSlugResolver((_, _) => null),
            TestBooklets.Printer(details, _renderer = new FakeBookletPdfRenderer(), blobs: _blobs), Options.Create(_options), _blobs, new FakeAiBudgetGuard());
        _adventureId = (await _views.CreateAsync(new CreateAdventureCommand(
            WorldId, GmUserId, WorldRole.GM, "The Slurping Spa", "Slimes have taken the old bathhouse.", null), default)).Value!.Adventure.Id;
    }

    private Nornis.Domain.Entities.Adventure Stored => _adventures.Adventures.Single();

    private Task<Nornis.Application.Errors.AppResult<CoverState>> GenerateAsync(string brief = Brief, WorldRole role = WorldRole.GM) =>
        _sut.GenerateAsync(new GenerateCoverCommand(WorldId, _adventureId, GmUserId, role, brief), default);

    [Test]
    public async Task TheBrief_ReadsTheTitlePitchOverviewAndSystem_AndNoOtherSection()
    {
        var overview = _adventures.Sections.Single(s => s.Kind == AdventureSectionKind.Overview);
        overview.BodyMarkdown = "The party is hired to reopen the baths.";
        _adventures.Sections.First(s => s.Kind == AdventureSectionKind.Scene).BodyMarkdown = "Secret: the owner is the slime queen.";
        _ai.Script(Brief);

        var state = (await _sut.WriteBriefAsync(new WriteCoverBriefCommand(WorldId, _adventureId, GmUserId, WorldRole.GM), default)).Value!;

        var message = _ai.Requests.Single().UserMessage;
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("The Slurping Spa"));
            Assert.That(message, Does.Contain("Slimes have taken the old bathhouse."));
            Assert.That(message, Does.Contain("reopen the baths"));
            Assert.That(message, Does.Contain("Shadowdark"));
            Assert.That(message, Does.Not.Contain("slime queen"));
            Assert.That(_ai.Requests.Single().SystemPrompt, Is.EqualTo(CoverArtPrompt.BriefSystemPrompt));
            Assert.That(state.Brief, Is.EqualTo(Brief));
            Assert.That(Stored.CoverBrief, Is.EqualTo(Brief));
            Assert.That(_ledger.Records.Single().OperationType, Is.EqualTo(AiOperationType.CoverArtBrief));
            Assert.That(Stored.AiUse.IsLocked(), Is.False, "a brief is never in the book");
        });
    }

    [Test]
    public async Task TheBrief_WaitsOnThePrepBudget()
    {
        _budget.PrepExceeded = true;

        var result = await _sut.WriteBriefAsync(new WriteCoverBriefCommand(WorldId, _adventureId, GmUserId, WorldRole.GM), default);

        Assert.That(result.Error!.Code, Is.EqualTo("prep_budget_exceeded"));
        Assert.That(_ai.Requests, Is.Empty);
    }

    [Test]
    public async Task Generate_MakesTwoOptions_WithTheRulesAfterTheBrief_AndCountsThem()
    {
        var state = (await GenerateAsync()).Value!;

        var call = _generator.Calls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(call.Count, Is.EqualTo(2));
            Assert.That(call.Prompt, Does.StartWith(Brief));
            Assert.That(call.Prompt, Does.EndWith(CoverArtPrompt.ImageRules));
            Assert.That(call.Scope.WorldId, Is.EqualTo(WorldId));
            Assert.That(state.Options, Has.Count.EqualTo(2));
            Assert.That(state.ImagesGenerated, Is.EqualTo(2));
            Assert.That(state.Brief, Is.EqualTo(Brief));
            Assert.That(state.Cover, Is.Null, "an option is not the cover until the GM picks it");
            Assert.That(state.Options.Select(id => CoverBlobs.OptionPath(WorldId, _adventureId, id)), Is.SubsetOf(_blobs.Blobs.Keys));
            Assert.That(_ledger.Records.Single().OperationType, Is.EqualTo(AiOperationType.CoverArt));
            Assert.That(_budget.PrepChecks, Is.EqualTo(1));
            Assert.That(Stored.AiUse.MadeArt, Is.False, "generating isn't using: only a chosen cover is in the book");
        });
    }

    [Test]
    public async Task TheCap_RefusesTheThirteenthImage_AndOnlyMakesWhatIsLeftBeforeIt()
    {
        _options.MaxCoverImagesPerAdventure = 5;
        await GenerateAsync();
        await GenerateAsync();

        var third = (await GenerateAsync()).Value!;
        var fourth = await GenerateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_generator.Calls.Select(c => c.Count), Is.EqualTo([2, 2, 1]));
            Assert.That(third.ImagesGenerated, Is.EqualTo(5));
            Assert.That(third.Options, Has.Count.EqualTo(5));
            Assert.That(third.CanGenerate, Is.False);
            Assert.That(third.GenerateBlocker, Does.Contain("upload your own art"));
            Assert.That(fourth.Error!.Code, Is.EqualTo("cover_cap"));
        });
    }

    [Test]
    public async Task TheDefaultCap_IsTwelveImages()
    {
        for (var i = 0; i < 6; i++)
        {
            Assert.That((await GenerateAsync()).IsSuccess, Is.True);
        }

        Assert.That((await GenerateAsync()).Error!.Code, Is.EqualTo("cover_cap"));
        Assert.That(Stored.CoverImagesGenerated, Is.EqualTo(12));
    }

    [Test]
    public async Task AFailedGeneration_GivesItsImagesBack_AndSaysWhy()
    {
        _generator.Throw = new AiKeyRequiredException(AiTask.CoverArt, "no OpenAI key");

        var result = await GenerateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("ai_key_required"));
            Assert.That(Stored.CoverImagesGenerated, Is.Zero);
            Assert.That(Stored.CoverOptionIds, Is.Empty);
            Assert.That(_ledger.Records, Is.Empty, "a refusal is not a call, so it isn't in the ledger");
        });
    }

    [Test]
    public async Task ASafetySystemBlock_AsksTheGmToReword()
    {
        _generator.Throw = new ImageArtRejectedException(moderation: true, "Your request was rejected by our safety system.");

        var result = await GenerateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("cover_refused"));
            Assert.That(result.Error.Message, Does.Contain("Try rewording"));
            Assert.That(Stored.CoverImagesGenerated, Is.Zero);
            Assert.That(_ledger.Records.Single().Succeeded, Is.False);
        });
    }

    [Test]
    public async Task ARejectionThatIsntModeration_ShowsOpenAisReason_NotReword()
    {
        _generator.Throw = new ImageArtRejectedException(moderation: false, "The model 'gpt-image-1' does not exist.");

        var result = await GenerateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("cover_rejected"));
            Assert.That(result.Error.Message, Does.Contain("The model 'gpt-image-1' does not exist."));
            Assert.That(result.Error.Message, Does.Not.Contain("reword"));
            Assert.That(Stored.CoverImagesGenerated, Is.Zero, "the count is given back");
        });
    }

    [Test]
    public async Task APlain400_IsUnavailable_NotARefusal()
    {
        _generator.Throw = new AiHttpException("HTTP 400", 400);

        var result = await GenerateAsync();

        Assert.That(result.Error!.Code, Is.EqualTo("service_unavailable"));
    }

    [Test]
    public async Task AShortDelivery_GivesBackTheImagesThatDidNotCome()
    {
        _generator.Deliver = 1;

        var state = (await GenerateAsync()).Value!;

        Assert.That(state.ImagesGenerated, Is.EqualTo(1));
        Assert.That(state.Options, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task WithNoKey_GenerateSaysToAddAnOpenAiKey()
    {
        _generator.HasKey = false;

        var state = (await _sut.GetAsync(WorldId, _adventureId, GmUserId, WorldRole.GM, default)).Value!;

        Assert.That(state.CanGenerate, Is.False);
        Assert.That(state.GenerateBlocker, Is.EqualTo(AdventureCoverService.NoKeyHint));
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task AnEmptyBrief_IsRefusedBeforeAnythingIsSpent(string brief)
    {
        var result = await GenerateAsync(brief);

        Assert.That(result.Error!.Code, Is.EqualTo("invalid_brief"));
        Assert.That(_generator.Calls, Is.Empty);
        Assert.That(Stored.CoverImagesGenerated, Is.Zero);
    }

    [Test]
    public async Task ChoosingAGeneratedOption_MakesItTheCover_AndRecordsAiArt()
    {
        var options = (await GenerateAsync()).Value!.Options;

        var state = (await _sut.ChooseAsync(new ChooseCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, options[1]), default)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(state.Cover!.Source, Is.EqualTo(CoverSource.Generated));
            Assert.That(state.Cover.Brief, Is.EqualTo(Brief));
            Assert.That(state.Cover.BlobPath, Is.EqualTo(CoverBlobs.OptionPath(WorldId, _adventureId, options[1])));
            Assert.That(state.ChosenOptionId, Is.EqualTo(options[1]));
            Assert.That(Stored.AiUse.MadeArt, Is.True);
            Assert.That(Stored.AiUse.Kind(), Is.EqualTo(AiUseKind.Art));
        });
    }

    [Test]
    public async Task ChoosingAnOptionThatIsNotThis_AdventuresIsNotFound()
    {
        var result = await _sut.ChooseAsync(new ChooseCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, Guid.NewGuid()), default);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(Stored.AiUse.MadeArt, Is.False);
    }

    [Test]
    public async Task UploadedArt_IsTheCover_AndRecordsNoAiUse()
    {
        var state = (await _sut.UploadAsync(new UploadCoverCommand(
            WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Jpeg(1200, 1800)), default)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(state.Cover!.Source, Is.EqualTo(CoverSource.Uploaded));
            Assert.That(state.Cover.ContentType, Is.EqualTo("image/jpeg"));
            Assert.That(state.Cover.BlobPath, Does.EndWith(".jpg"));
            Assert.That(_blobs.Blobs.ContainsKey(state.Cover.BlobPath), Is.True);
            Assert.That(state.Warning, Is.Null);
            Assert.That(Stored.AiUse.IsLocked(), Is.False);
        });
    }

    [Test]
    public async Task ReplacingAGeneratedCoverWithAnUpload_KeepsTheAiArtRecord()
    {
        var options = (await GenerateAsync()).Value!.Options;
        await _sut.ChooseAsync(new ChooseCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, options[0]), default);

        await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Png(1200, 1800)), default);
        await _sut.RemoveAsync(new RemoveCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM), default);

        Assert.That(Stored.AiUse.MadeArt, Is.True, "nothing clears the record once AI art was in the book");
        Assert.That(Stored.Cover, Is.Null);
    }

    [Test]
    public async Task ASmallUpload_IsTakenWithAWarning()
    {
        var state = (await _sut.UploadAsync(new UploadCoverCommand(
            WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Png(600, 900)), default)).Value!;

        Assert.That(state.Cover, Is.Not.Null);
        Assert.That(state.Warning, Does.Contain("600 × 900"));
    }

    [Test]
    public async Task AnUploadThatIsNotAPngOrJpeg_OrTooLarge_IsRefused()
    {
        var gif = await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, "GIF89a......"u8.ToArray()), default);
        _options.MaxCoverUploadBytes = 20;
        var large = await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Png(1200, 1800)), default);

        Assert.That(gif.Error!.Code, Is.EqualTo("invalid_image"));
        Assert.That(large.Error!.Code, Is.EqualTo("image_too_large"));
        Assert.That(_blobs.Blobs, Is.Empty);
    }

    [Test]
    public async Task ANewUpload_DeletesTheUploadItReplaced_ButNeverAGeneratedOption()
    {
        var first = (await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Png(1200, 1800)), default)).Value!;
        var options = (await GenerateAsync()).Value!.Options;
        await _sut.ChooseAsync(new ChooseCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, options[0]), default);
        await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Png(1200, 1800)), default);

        Assert.That(_blobs.DeletedPaths, Is.EqualTo([first.Cover!.BlobPath]));
        Assert.That(_blobs.Blobs.ContainsKey(CoverBlobs.OptionPath(WorldId, _adventureId, options[0])), Is.True);
    }

    [Test]
    public async Task TheLayout_IsKeptAcrossANewCover()
    {
        await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Png(1200, 1800)), default);
        await _sut.SetLayoutAsync(new SetCoverLayoutCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, CoverLayout.Framed), default);

        var state = (await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Png(1200, 1800)), default)).Value!;

        Assert.That(state.Cover!.Layout, Is.EqualTo(CoverLayout.Framed));
    }

    [Test]
    public async Task ALayoutWithNoArt_IsRefused()
    {
        var result = await _sut.SetLayoutAsync(new SetCoverLayoutCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, CoverLayout.Framed), default);

        Assert.That(result.Error!.Code, Is.EqualTo("no_cover"));
    }

    [Test]
    public async Task DeletingTheAdventure_DeletesItsCoverFolder()
    {
        await GenerateAsync();

        await _views.DeleteAsync(new DeleteAdventureCommand(WorldId, _adventureId, WorldRole.GM), default);

        Assert.That(_blobs.DeletedPrefixes, Is.EqualTo([CoverBlobs.Folder(WorldId, _adventureId)]));
        Assert.That(_blobs.Blobs.Keys, Has.None.StartsWith(CoverBlobs.Folder(WorldId, _adventureId)));
    }

    [Test]
    public async Task EveryCoverRoute_IsGmOnly()
    {
        var results = new[]
        {
            (await _sut.GetAsync(WorldId, _adventureId, GmUserId, WorldRole.Player, default)).Error,
            (await _sut.WriteBriefAsync(new WriteCoverBriefCommand(WorldId, _adventureId, GmUserId, WorldRole.Player), default)).Error,
            (await GenerateAsync(role: WorldRole.Player)).Error,
            (await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.Player, TestImages.Png(1200, 1800)), default)).Error,
            (await _sut.OpenCoverAsync(WorldId, _adventureId, WorldRole.Player, default)).Error,
        };

        Assert.That(results.Select(e => e?.StatusCode), Is.All.EqualTo(403));
        Assert.That(_generator.Calls, Is.Empty);
    }

    [Test]
    public async Task AnotherWorldsAdventure_IsNotFound()
    {
        var result = await _sut.GetAsync(Guid.NewGuid(), _adventureId, GmUserId, WorldRole.GM, default);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task ThePrint_CarriesTheCoverArtInItsLayout()
    {
        var png = TestImages.Png(1200, 1800, salt: 7);
        await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, png), default);
        await _sut.SetLayoutAsync(new SetCoverLayoutCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, CoverLayout.ClassicModule), default);

        await _views.GetBookletAsync(WorldId, _adventureId, WorldRole.GM, default);

        var art = _renderer.Rendered.Single().Cover.Art!;
        Assert.That(art.Image, Is.EqualTo(png));
        Assert.That(art.Layout, Is.EqualTo(CoverLayout.ClassicModule));
    }

    [Test]
    public async Task ACoverWhoseBlobIsGone_PrintsTheTypesetCover_RatherThanFailing()
    {
        var state = (await _sut.UploadAsync(new UploadCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, TestImages.Png(1200, 1800)), default)).Value!;
        await _blobs.DeleteBlobAsync(state.Cover!.BlobPath);

        var booklet = await _views.GetBookletAsync(WorldId, _adventureId, WorldRole.GM, default);

        Assert.That(booklet.IsSuccess, Is.True);
        Assert.That(_renderer.Rendered.Single().Cover.Art, Is.Null);
    }

    [Test]
    public async Task WithNoArt_ThePrintKeepsTheTypesetCover()
    {
        await _views.GetBookletAsync(WorldId, _adventureId, WorldRole.GM, default);

        Assert.That(_renderer.Rendered.Single().Cover.Art, Is.Null);
    }

    // ---------------------------------------------------------------- refine (feature 45) --

    private Task<Nornis.Application.Errors.AppResult<CoverState>> RefineAsync(Guid option, WorldRole role = WorldRole.GM) =>
        _sut.RefineAsync(new RefineCoverCommand(WorldId, _adventureId, GmUserId, role, option), default);

    [Test]
    public async Task Refine_SendsTheOptionAndTheBrief_AndAddsTheResultFirst_LeavingTheCoverAlone()
    {
        var drafts = (await GenerateAsync()).Value!;
        var draft = drafts.Options[1];
        await _sut.ChooseAsync(new ChooseCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, drafts.Options[0]), default);

        var state = (await RefineAsync(draft)).Value!;

        var refine = _generator.Refines.Single();
        Assert.Multiple(() =>
        {
            Assert.That(refine.Source, Is.EqualTo(_blobs.Blobs[CoverBlobs.OptionPath(WorldId, _adventureId, draft)].Content));
            Assert.That(refine.Prompt, Does.StartWith(ArtRefinePrompt.Instruction));
            Assert.That(refine.Prompt, Does.Contain(Brief));
            Assert.That(refine.Prompt, Does.EndWith(CoverArtPrompt.ImageRules), "a refine can't drop the no-text rule");
            Assert.That(refine.Shape, Is.EqualTo(ImageShape.Portrait));
            Assert.That(refine.Operation, Is.EqualTo(nameof(AiOperationType.CoverArtRefinement)));
            Assert.That(state.Options, Has.Count.EqualTo(3));
            Assert.That(state.Options.Skip(1), Is.EqualTo(drafts.Options), "the refined option comes first; the drafts stay");
            Assert.That(_blobs.Blobs.Keys, Does.Contain(CoverBlobs.OptionPath(WorldId, _adventureId, state.Options[0])));
            Assert.That(state.ChosenOptionId, Is.EqualTo(drafts.Options[0]), "the cover is the GM's to change");
            Assert.That(state.ImagesGenerated, Is.EqualTo(3));
            Assert.That(state.Brief, Is.EqualTo(Brief));
            Assert.That(_ledger.Records.Select(r => r.OperationType), Does.Contain(AiOperationType.CoverArtRefinement));
            Assert.That(_budget.PrepChecks, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ARefinedOption_IsChosenLikeAnyOther_AndRecordsAiArt()
    {
        var draft = (await GenerateAsync()).Value!.Options[0];
        var refined = (await RefineAsync(draft)).Value!.Options[0];

        var state = (await _sut.ChooseAsync(new ChooseCoverCommand(WorldId, _adventureId, GmUserId, WorldRole.GM, refined), default)).Value!;

        Assert.That(state.ChosenOptionId, Is.EqualTo(refined));
        Assert.That(Stored.AiUse.MadeArt, Is.True);
    }

    [Test]
    public async Task Refine_CountsOneImage_AgainstTheSameCap()
    {
        _options.MaxCoverImagesPerAdventure = 3;
        var draft = (await GenerateAsync()).Value!.Options[0];

        var first = await RefineAsync(draft);
        var second = await RefineAsync(draft);

        Assert.Multiple(() =>
        {
            Assert.That(first.Value!.ImagesGenerated, Is.EqualTo(3));
            Assert.That(second.Error!.Code, Is.EqualTo("cover_cap"));
            Assert.That(_generator.Refines, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AFailedRefine_GivesItsImageBack_AndAddsNothing()
    {
        var drafts = (await GenerateAsync()).Value!;
        _generator.Throw = new ImageArtRejectedException(moderation: true, "safety system");

        var result = await RefineAsync(drafts.Options[0]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("cover_refused"));
            Assert.That(Stored.CoverImagesGenerated, Is.EqualTo(2));
            Assert.That(Stored.CoverOptionIds, Is.EquivalentTo(drafts.Options));
            Assert.That(_ledger.Records.Last().OperationType, Is.EqualTo(AiOperationType.CoverArtRefinement));
            Assert.That(_ledger.Records.Last().Succeeded, Is.False);
        });
    }

    [Test]
    public async Task ARefineThatSendsNothingBack_GivesItsImageBack()
    {
        var drafts = (await GenerateAsync()).Value!;
        _generator.Deliver = 0;

        var result = await RefineAsync(drafts.Options[0]);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(503));
        Assert.That(Stored.CoverImagesGenerated, Is.EqualTo(2));
        Assert.That(Stored.CoverOptionIds, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task RefiningAnOptionThatIsntOnOffer_OrWhosePictureIsGone_IsNotFound_AndCostsNothing()
    {
        var draft = (await GenerateAsync()).Value!.Options[0];
        _blobs.Blobs.Remove(CoverBlobs.OptionPath(WorldId, _adventureId, draft));

        var unknown = await RefineAsync(Guid.NewGuid());
        var gone = await RefineAsync(draft);

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Error!.StatusCode, Is.EqualTo(404));
            Assert.That(gone.Error!.StatusCode, Is.EqualTo(404));
            Assert.That(Stored.CoverImagesGenerated, Is.EqualTo(2), "a missing picture gives its count back");
            Assert.That(_generator.Refines, Is.Empty);
        });
    }

    [Test]
    public async Task Refine_IsGmOnly()
    {
        var draft = (await GenerateAsync()).Value!.Options[0];

        var result = await RefineAsync(draft, WorldRole.Player);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_generator.Refines, Is.Empty);
    }
}
