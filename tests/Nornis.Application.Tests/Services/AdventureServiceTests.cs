using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class AdventureServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    private InMemoryAdventureRepository _adventures = null!;
    private InMemoryCampaignRepository _campaigns = null!;
    private PrepOptions _options = null!;
    private AdventureService _sut = null!;
    private FakeBookletPdfRenderer _renderer = null!;
    private FakeAiBudgetGuard _budget = null!;

    [SetUp]
    public void SetUp()
    {
        _adventures = new InMemoryAdventureRepository();
        _campaigns = new InMemoryCampaignRepository();
        _options = new PrepOptions();
        var slugs = new FakeSlugResolver((world, slug) =>
            _adventures.Adventures.FirstOrDefault(a => a.WorldId == world && a.Slug == slug)?.Id);
        _renderer = new FakeBookletPdfRenderer();
        _budget = new FakeAiBudgetGuard();
        _sut = new AdventureService(_adventures, _campaigns, slugs,
            TestBooklets.Printer(TestPrintDetails.For(WorldId, GmUserId, "Shadowdark"), _renderer), Options.Create(_options), new FakeBlobStorageService(), _budget);
    }

    private async Task<AdventureView> CreateAsync(string title = "The Silver Key", Guid? campaignId = null)
    {
        var result = await _sut.CreateAsync(
            new CreateAdventureCommand(WorldId, GmUserId, WorldRole.GM, title, "A key in the mud.", campaignId),
            CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return result.Value!;
    }

    private static AdventureSection SectionOf(AdventureView view, AdventureSectionKind kind) =>
        view.Adventure.Sections.First(s => s.Kind == kind);

    // ------------------------------------------------------------ GM only --

    /// <summary>
    /// Every operation, as one row each, so a new operation that skips the role check shows up
    /// here as a missing row rather than as a pass. Player, Observer, and a GM viewing as a
    /// player (whose effective role reaches the service as Player) are all refused.
    /// </summary>
    private static IEnumerable<TestCaseData> EveryOperation()
    {
        var ops = new (string Name, Func<AdventureService, Guid, Guid, WorldRole, Task<AppError?>> Run)[]
        {
            ("List", async (s, _, _, r) => (await s.ListAsync(WorldId, r, default)).Error),
            ("Get", async (s, a, _, r) => (await s.GetAsync(WorldId, a, r, default)).Error),
            ("GetByKey", async (s, _, _, r) => (await s.GetByKeyAsync(WorldId, "no-such-slug", r, default)).Error),
            ("Create", async (s, _, _, r) => (await s.CreateAsync(new(WorldId, GmUserId, r, "T", null, null), default)).Error),
            ("Import", async (s, _, _, r) => (await s.ImportAsync(new(WorldId, GmUserId, r, [new("f.md", "# T")], null), default)).Error),
            ("Update", async (s, a, _, r) => (await s.UpdateAsync(new(WorldId, a, r, "T", null, null), default)).Error),
            ("SaveSection", async (s, a, sec, r) => (await s.SaveSectionAsync(
                new(WorldId, a, sec, r, AdventureSectionKind.Notes, "T", "Body", DateTimeOffset.UtcNow), default)).Error),
            ("AddSection", async (s, a, _, r) => (await s.AddSectionAsync(new(WorldId, a, r, AdventureSectionKind.Notes, "T", null), default)).Error),
            ("DeleteSection", async (s, a, sec, r) => (await s.DeleteSectionAsync(new(WorldId, a, sec, r), default)).Error),
            ("Reorder", async (s, a, _, r) => (await s.ReorderAsync(new(WorldId, a, r, []), default)).Error),
            ("Delete", async (s, a, _, r) => (await s.DeleteAsync(new(WorldId, a, r), default)).Error),
            ("Undo", async (s, a, sec, r) => (await s.UndoAsync(new(WorldId, a, sec, r), default)).Error),
        };

        foreach (var (name, run) in ops)
        {
            foreach (var role in new[] { WorldRole.Player, WorldRole.Observer })
            {
                yield return new TestCaseData(run, role).SetName($"{name}_As{role}_IsRefused");
            }
        }
    }

    [TestCaseSource(nameof(EveryOperation))]
    [Category("Authorization")]
    public async Task EveryOperation_RefusesAnyoneButTheGm(
        Func<AdventureService, Guid, Guid, WorldRole, Task<AppError?>> operation, WorldRole role)
    {
        var view = await CreateAsync();
        var section = view.Adventure.Sections.First();

        var error = await operation(_sut, view.Adventure.Id, section.Id, role);

        Assert.That(error?.StatusCode, Is.EqualTo(403));
        Assert.That(_adventures.Adventures, Has.Count.EqualTo(1), "a refused call must not have written anything");
        Assert.That(_adventures.Sections.Single(s => s.Id == section.Id).BodyMarkdown, Is.EqualTo(section.BodyMarkdown));
    }

    // ------------------------------------------------------------- Create --

    [Test]
    public async Task Create_LaysDownTheHouseSkeleton()
    {
        var view = await CreateAsync();

        Assert.That(view.Adventure.Sections.Select(s => (s.Kind, s.Title, s.SortOrder)), Is.EqualTo(
        [
            (AdventureSectionKind.Overview, "Overview", 1),
            (AdventureSectionKind.SessionClock, "Session clock", 2),
            (AdventureSectionKind.Chapter, "Chapter 1", 3),
            (AdventureSectionKind.Scene, "Scene 1", 4),
            (AdventureSectionKind.Scene, "Scene 2", 5),
            (AdventureSectionKind.Scene, "Scene 3", 6),
            (AdventureSectionKind.QuickReference, "Quick reference", 7),
        ]));
        Assert.That(view.Adventure.Sections.Where(s => s.Kind == AdventureSectionKind.Scene).Select(s => s.BodyMarkdown),
            Is.All.EqualTo(AdventureSkeleton.SceneTemplate));
        Assert.That(view.Adventure.Pitch, Is.EqualTo("A key in the mud."));
    }

    [Test]
    public async Task Create_ACampaignFromAnotherWorld_IsRefused()
    {
        var elsewhere = new Campaign { Id = Guid.NewGuid(), WorldId = Guid.NewGuid(), Name = "Missing Caravan Arc" };
        _campaigns.Seed(elsewhere);

        var result = await _sut.CreateAsync(
            new CreateAdventureCommand(WorldId, GmUserId, WorldRole.GM, "T", null, elsewhere.Id), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("invalid_campaign"));
        Assert.That(_adventures.Adventures, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Create_WithoutATitle_IsRefused(string title)
    {
        var result = await _sut.CreateAsync(
            new CreateAdventureCommand(WorldId, GmUserId, WorldRole.GM, title, null, null), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Get_AnAdventureOfAnotherWorld_IsNotFound()
    {
        var view = await CreateAsync();

        var result = await _sut.GetAsync(Guid.NewGuid(), view.Adventure.Id, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task GetByKey_FindsTheAdventureBySlug()
    {
        var view = await CreateAsync();

        var result = await _sut.GetByKeyAsync(WorldId, view.Adventure.Slug!, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Adventure.Id, Is.EqualTo(view.Adventure.Id));
    }

    [Test]
    public async Task List_CountsAnUntouchedSceneTemplate_AsUnfilled()
    {
        var view = await CreateAsync();
        var overview = SectionOf(view, AdventureSectionKind.Overview);
        await _sut.SaveSectionAsync(new SaveSectionCommand(WorldId, view.Adventure.Id, overview.Id, WorldRole.GM,
            overview.Kind, overview.Title, "Voss has the key.", overview.UpdatedAt), CancellationToken.None);

        var list = (await _sut.ListAsync(WorldId, WorldRole.GM, CancellationToken.None)).Value!;

        Assert.That(list.Single().SectionCount, Is.EqualTo(7));
        Assert.That(list.Single().FilledSectionCount, Is.EqualTo(1));
    }

    // ------------------------------------------------------------- Import --

    [Test]
    public async Task Import_MakesAnAdventureOfTheFiles()
    {
        var result = await _sut.ImportAsync(new ImportAdventureCommand(WorldId, GmUserId, WorldRole.GM,
            [new("a.md", "# Black Harbor\n## Dusk\n\nFog.\n\n# Scene 1: Customs\n\nClerk.")], null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.Adventure.Title, Is.EqualTo("Black Harbor"));
        Assert.That(result.Value.Adventure.Sections.Select(s => (s.Kind, s.Title, s.SortOrder)), Is.EqualTo(
        [
            (AdventureSectionKind.Chapter, "Dusk", 1),
            (AdventureSectionKind.Scene, "Scene 1: Customs", 2),
        ]));
    }

    [Test]
    public async Task Import_StoresTheFrontMattersDetails_AndReportsWhatItLeftOut()
    {
        var result = await _sut.ImportAsync(new ImportAdventureCommand(WorldId, GmUserId, WorldRole.GM,
            [new("a.md", "---\nsystem: OSE\nyear: 2025\npublisher: Kobold\n---\n# Black Harbor\n\nFog.")], null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(_adventures.Adventures.Single().Details, Is.EqualTo(new AdventureDetails { GameSystem = "OSE", Year = 2025 }));
            Assert.That(result.Value!.IgnoredDetails, Is.EqualTo((string[])["publisher"]));
            Assert.That(result.Value.PrintDetails.GameSystem, Is.EqualTo("OSE"));
        });
    }

    [Test]
    public async Task Import_ASectionOverItsBound_IsRefused_NamingTheSizeAndTheBound()
    {
        _options.MaxSectionChars = 10;

        var result = await _sut.ImportAsync(new ImportAdventureCommand(WorldId, GmUserId, WorldRole.GM,
            [new("a.md", "# Black Harbor\n\n# Scene 1: Customs\n\n" + new string('x', 11))], null), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_too_large"));
        Assert.That(result.Error!.Message, Does.Contain("Scene 1: Customs").And.Contain("11").And.Contain("10"));
        Assert.That(_adventures.Adventures, Is.Empty);
    }

    [Test]
    public async Task Import_MoreSectionsThanTheBound_IsRefused()
    {
        _options.MaxSections = 2;

        var result = await _sut.ImportAsync(new ImportAdventureCommand(WorldId, GmUserId, WorldRole.GM,
            [new("a.md", "# T\n\na\n\n# B\n\nb\n\n# C\n\nc")], null), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_too_large"));
        Assert.That(result.Error!.Message, Does.Contain("3").And.Contain("2"));
    }

    [Test]
    public async Task Import_RawTextOverTwiceTheAdventureBound_IsRefusedBeforeParsing()
    {
        _options.MaxAdventureChars = 10;

        var result = await _sut.ImportAsync(new ImportAdventureCommand(WorldId, GmUserId, WorldRole.GM,
            [new("a.md", new string('x', 21))], null), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_too_large"));
    }

    // -------------------------------------------------------- SaveSection --

    [Test]
    public async Task SaveSection_WritesTheEdit_AndMovesTheStamp()
    {
        var view = await CreateAsync();
        var scene = SectionOf(view, AdventureSectionKind.Scene);

        var result = await _sut.SaveSectionAsync(new SaveSectionCommand(WorldId, view.Adventure.Id, scene.Id, WorldRole.GM,
            AdventureSectionKind.Scene, "Scene 1: The Customs House", "> Fog.", scene.UpdatedAt), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var stored = _adventures.Sections.Single(s => s.Id == scene.Id);
        Assert.That(stored.BodyMarkdown, Is.EqualTo("> Fog."));
        Assert.That(stored.Title, Is.EqualTo("Scene 1: The Customs House"));
        Assert.That(stored.UpdatedAt, Is.GreaterThan(scene.UpdatedAt));
        Assert.That(result.Value!.UpdatedAt, Is.EqualTo(stored.UpdatedAt));
    }

    [Test]
    public async Task SaveSection_BasedOnAStaleCopy_IsAConflict_AndWritesNothing()
    {
        var view = await CreateAsync();
        var scene = SectionOf(view, AdventureSectionKind.Scene);
        await _sut.SaveSectionAsync(new SaveSectionCommand(WorldId, view.Adventure.Id, scene.Id, WorldRole.GM,
            scene.Kind, scene.Title, "First.", scene.UpdatedAt), CancellationToken.None);

        var result = await _sut.SaveSectionAsync(new SaveSectionCommand(WorldId, view.Adventure.Id, scene.Id, WorldRole.GM,
            scene.Kind, scene.Title, "Second, from an old tab.", scene.UpdatedAt), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(409));
        Assert.That(_adventures.Sections.Single(s => s.Id == scene.Id).BodyMarkdown, Is.EqualTo("First."));
    }

    [Test]
    public async Task SaveSection_OverTheSectionBound_IsRefused()
    {
        _options.MaxSectionChars = 5;
        var view = await CreateAsync();
        var overview = SectionOf(view, AdventureSectionKind.Overview);

        var result = await _sut.SaveSectionAsync(new SaveSectionCommand(WorldId, view.Adventure.Id, overview.Id, WorldRole.GM,
            overview.Kind, overview.Title, "123456", overview.UpdatedAt), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_too_large"));
        Assert.That(result.Error!.Message, Does.Contain("6").And.Contain("5"));
    }

    [Test]
    public async Task SaveSection_PushingTheAdventureOverItsBound_IsRefused()
    {
        var view = await CreateAsync();
        _options.MaxAdventureChars = AdventureSize.Measure(view.Adventure.Sections) + 3;
        var overview = SectionOf(view, AdventureSectionKind.Overview);

        var result = await _sut.SaveSectionAsync(new SaveSectionCommand(WorldId, view.Adventure.Id, overview.Id, WorldRole.GM,
            overview.Kind, overview.Title, "1234", overview.UpdatedAt), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_too_large"));
    }

    [Test]
    public async Task SaveSection_OfAnotherAdventure_IsNotFound()
    {
        var mine = await CreateAsync();
        var other = await CreateAsync("Another");
        var foreign = other.Adventure.Sections.First();

        var result = await _sut.SaveSectionAsync(new SaveSectionCommand(WorldId, mine.Adventure.Id, foreign.Id, WorldRole.GM,
            foreign.Kind, foreign.Title, "x", foreign.UpdatedAt), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
    }

    // --------------------------------------------- Add, delete, reorder --

    [Test]
    public async Task AddSection_AfterASection_LandsThere_AndRenumbers()
    {
        var view = await CreateAsync();
        var chapter = SectionOf(view, AdventureSectionKind.Chapter);

        var result = await _sut.AddSectionAsync(new AddSectionCommand(WorldId, view.Adventure.Id, WorldRole.GM,
            AdventureSectionKind.Scene, "Scene 0: Prologue", chapter.Id), CancellationToken.None);

        var titles = result.Value!.Adventure.Sections.Select(s => s.Title).ToList();
        Assert.That(titles.IndexOf("Scene 0: Prologue"), Is.EqualTo(titles.IndexOf("Chapter 1") + 1));
        Assert.That(result.Value.Adventure.Sections.Select(s => s.SortOrder), Is.EqualTo(Enumerable.Range(1, 8)));
        Assert.That(result.Value.Adventure.Sections.Single(s => s.Title == "Scene 0: Prologue").BodyMarkdown,
            Is.EqualTo(AdventureSkeleton.SceneTemplate));
    }

    [Test]
    public async Task AddSection_PastTheSectionBound_IsRefused()
    {
        _options.MaxSections = 7;
        var view = await CreateAsync();

        var result = await _sut.AddSectionAsync(new AddSectionCommand(WorldId, view.Adventure.Id, WorldRole.GM,
            AdventureSectionKind.Notes, "One too many", null), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_too_large"));
    }

    [Test]
    public async Task DeleteSection_RemovesItAndItsRevisions()
    {
        var view = await CreateAsync();
        var scene = SectionOf(view, AdventureSectionKind.Scene);
        _adventures.SeedRevision(Revision(scene.Id, "old", DateTimeOffset.UtcNow));

        var result = await _sut.DeleteSectionAsync(
            new DeleteSectionCommand(WorldId, view.Adventure.Id, scene.Id, WorldRole.GM), CancellationToken.None);

        Assert.That(result.Value!.Adventure.Sections.Select(s => s.Id), Does.Not.Contain(scene.Id));
        Assert.That(result.Value.Adventure.Sections.Select(s => s.SortOrder), Is.EqualTo(Enumerable.Range(1, 6)));
        Assert.That(_adventures.Revisions, Is.Empty);
    }

    [Test]
    public async Task Reorder_WritesTheGivenOrder()
    {
        var view = await CreateAsync();
        var reversed = view.Adventure.Sections.Select(s => s.Id).Reverse().ToList();

        var result = await _sut.ReorderAsync(
            new ReorderSectionsCommand(WorldId, view.Adventure.Id, WorldRole.GM, reversed), CancellationToken.None);

        Assert.That(result.Value!.Adventure.Sections.Select(s => s.Id), Is.EqualTo(reversed));
    }

    [Test]
    public async Task Reorder_ThatLeavesASectionOut_IsRefused()
    {
        var view = await CreateAsync();
        var partial = view.Adventure.Sections.Select(s => s.Id).Skip(1).ToList();

        var result = await _sut.ReorderAsync(
            new ReorderSectionsCommand(WorldId, view.Adventure.Id, WorldRole.GM, partial), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Reorder_ThatRepeatsASection_IsRefused()
    {
        var view = await CreateAsync();
        var ids = view.Adventure.Sections.Select(s => s.Id).ToList();
        ids[^1] = ids[0];

        var result = await _sut.ReorderAsync(
            new ReorderSectionsCommand(WorldId, view.Adventure.Id, WorldRole.GM, ids), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(400));
    }

    // ------------------------------------------------------------- Update --

    [Test]
    public async Task Update_ReplacesTitlePitchAndCampaign_NullMeaningNone()
    {
        var campaign = new Campaign { Id = Guid.NewGuid(), WorldId = WorldId, Name = "Missing Caravan Arc" };
        _campaigns.Seed(campaign);
        var view = await CreateAsync(campaignId: campaign.Id);

        var result = await _sut.UpdateAsync(
            new UpdateAdventureCommand(WorldId, view.Adventure.Id, WorldRole.GM, "  The Silver Key, revised ", null, null),
            CancellationToken.None);

        var stored = _adventures.Adventures.Single();
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(stored.Title, Is.EqualTo("The Silver Key, revised"));
        Assert.That(stored.Pitch, Is.Null);
        Assert.That(stored.CampaignId, Is.Null);
    }

    // ------------------------------------------------------------ Details --

    [Test]
    public async Task UpdateDetails_SavesTheFour_Normalised_AndReturnsThePreview()
    {
        var view = await CreateAsync();

        var result = await _sut.UpdateDetailsAsync(
            new UpdateAdventureDetailsCommand(WorldId, view.Adventure.Id, WorldRole.GM, "  ", " 1st ", "1–3", 2026),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(_adventures.Adventures.Single().Details,
                Is.EqualTo(new AdventureDetails { Edition = "1st", Levels = "1–3", Year = 2026 }));
            Assert.That(result.Value!.Adventure.Details.Edition, Is.EqualTo("1st"));

            // The preview: the world's game system, the fixed type and publisher, the creator as author.
            var preview = result.Value.PrintDetails;
            Assert.That(preview.Title, Is.EqualTo("The Silver Key"));
            Assert.That(preview.GameSystem, Is.EqualTo("Shadowdark"));
            Assert.That(preview.Type, Is.EqualTo(PrintDetails.NornisType));
            Assert.That(preview.Publisher, Is.EqualTo(PrintDetails.NornisPublisher));
            Assert.That(preview.Author, Is.EqualTo(TestPrintDetails.Username));
            Assert.That(preview.Year, Is.EqualTo(2026));
        });
    }

    // ----------------------------------------------------------------- Typography --

    [Test]
    public async Task UpdateTypography_SavesTheGmsType_AndAnswersWithIt()
    {
        var view = await CreateAsync();

        var result = await _sut.UpdateTypographyAsync(
            new UpdateBookTypographyCommand(WorldId, view.Adventure.Id, WorldRole.GM, BookFont.EbGaramond, 11.5m, 1.45m),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var expected = new BookTypography { Font = BookFont.EbGaramond, Size = 11.5m, LineSpacing = 1.45m };
        Assert.Multiple(() =>
        {
            Assert.That(_adventures.Adventures.Single().Typography, Is.EqualTo(expected));
            Assert.That(result.Value!.Adventure.Typography, Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task UpdateTypography_AllNull_IsTheDefaultAgain()
    {
        var view = await CreateAsync();
        await _sut.UpdateTypographyAsync(
            new UpdateBookTypographyCommand(WorldId, view.Adventure.Id, WorldRole.GM, BookFont.Lora, 12m, 1.5m), CancellationToken.None);

        await _sut.UpdateTypographyAsync(
            new UpdateBookTypographyCommand(WorldId, view.Adventure.Id, WorldRole.GM, null, null, null), CancellationToken.None);

        var stored = _adventures.Adventures.Single().Typography;
        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo(new BookTypography()));
            Assert.That(stored.FontOrDefault, Is.EqualTo(BookFont.Newsreader));
            Assert.That(stored.SizeOrDefault, Is.EqualTo(10m));
            Assert.That(stored.LineSpacingOrDefault, Is.EqualTo(1.3m));
        });
    }

    [TestCase(7.5, null)]
    [TestCase(14.5, null)]
    [TestCase(10.25, null)]
    [TestCase(null, 0.9)]
    [TestCase(null, 2.05)]
    [TestCase(null, 1.33)]
    public async Task UpdateTypography_OutsideItsBounds_Is400_AndSavesNothing(double? size, double? spacing)
    {
        var view = await CreateAsync();
        var before = _adventures.Adventures.Single().UpdatedAt;

        var result = await _sut.UpdateTypographyAsync(
            new UpdateBookTypographyCommand(WorldId, view.Adventure.Id, WorldRole.GM, null, (decimal?)size, (decimal?)spacing),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(result.Error.Code, Is.EqualTo("invalid_typography"));
            Assert.That(_adventures.Adventures.Single().Typography, Is.EqualTo(new BookTypography()));
            Assert.That(_adventures.Adventures.Single().UpdatedAt, Is.EqualTo(before));
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task UpdateTypography_ByAnyoneButTheGm_Is403_AndSavesNothing(WorldRole role)
    {
        var view = await CreateAsync();

        var result = await _sut.UpdateTypographyAsync(
            new UpdateBookTypographyCommand(WorldId, view.Adventure.Id, role, BookFont.Lora, null, null), CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_adventures.Adventures.Single().Typography, Is.EqualTo(new BookTypography()));
    }

    [Test]
    public async Task UpdateTypography_InAnotherWorld_Is404()
    {
        var view = await CreateAsync();

        var result = await _sut.UpdateTypographyAsync(
            new UpdateBookTypographyCommand(Guid.NewGuid(), view.Adventure.Id, WorldRole.GM, BookFont.Lora, null, null),
            CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ------------------------------------------------------------------ AI use (feature 38) --

    [Test]
    public async Task TheGmsOwnTick_SetsAndClearsDeclared_AndThePreviewSaysSo()
    {
        var view = await CreateAsync();

        var ticked = await _sut.SetAiDeclaredAsync(new SetAiDeclaredCommand(WorldId, view.Adventure.Id, WorldRole.GM, true), CancellationToken.None);
        var tickedKind = ticked.Value!.PrintDetails.AiUse;
        var unticked = await _sut.SetAiDeclaredAsync(new SetAiDeclaredCommand(WorldId, view.Adventure.Id, WorldRole.GM, false), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(tickedKind, Is.EqualTo(AiUseKind.Declared));
            Assert.That(unticked.Value!.PrintDetails.AiUse, Is.EqualTo(AiUseKind.None));
            Assert.That(_adventures.Adventures.Single().AiUse.Declared, Is.False);
        });
    }

    [Test]
    public async Task OnceAiUseIsRecorded_TheTickIsLocked_AndUntickingChangesNothing()
    {
        var view = await CreateAsync();
        await _sut.SetAiDeclaredAsync(new SetAiDeclaredCommand(WorldId, view.Adventure.Id, WorldRole.GM, true), CancellationToken.None);
        var section = _adventures.Sections.First(s => s.AdventureId == view.Adventure.Id);
        await _adventures.TryAcceptDraftAsync(
            new AdventureSectionRevision { Id = Guid.NewGuid(), SectionId = section.Id, CreatedAt = DateTimeOffset.UtcNow },
            "The Loremaster's words.", 20, section.UpdatedAt, DateTimeOffset.UtcNow);

        var result = await _sut.SetAiDeclaredAsync(new SetAiDeclaredCommand(WorldId, view.Adventure.Id, WorldRole.GM, false), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_adventures.Adventures.Single().AiUse.Declared, Is.True, "locked: the tick stays as it was");
            Assert.That(result.Value!.PrintDetails.AiUse, Is.EqualTo(AiUseKind.Text));
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task TheAiTick_IsGmOnly(WorldRole role)
    {
        var view = await CreateAsync();

        var result = await _sut.SetAiDeclaredAsync(new SetAiDeclaredCommand(WorldId, view.Adventure.Id, role, true), CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_adventures.Adventures.Single().AiUse.Declared, Is.False);
    }

    [Test]
    public async Task UpdateDetails_NullClearsAField()
    {
        var view = await CreateAsync();
        await _sut.UpdateDetailsAsync(
            new UpdateAdventureDetailsCommand(WorldId, view.Adventure.Id, WorldRole.GM, "OSE", "2nd", "3", 2025), CancellationToken.None);

        var result = await _sut.UpdateDetailsAsync(
            new UpdateAdventureDetailsCommand(WorldId, view.Adventure.Id, WorldRole.GM, null, "2nd", "3", 2025), CancellationToken.None);

        Assert.That(_adventures.Adventures.Single().Details.GameSystem, Is.Null);
        Assert.That(result.Value!.PrintDetails.GameSystem, Is.EqualTo("Shadowdark"), "cleared means the world's again");
    }

    [Test]
    public async Task UpdateDetails_WithOneFieldOutOfBounds_SavesNothing()
    {
        var view = await CreateAsync();
        var before = _adventures.Adventures.Single().UpdatedAt;

        var result = await _sut.UpdateDetailsAsync(
            new UpdateAdventureDetailsCommand(WorldId, view.Adventure.Id, WorldRole.GM, "OSE", "2nd", "3", 1066),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(result.Error.Code, Is.EqualTo("invalid_details"));
            Assert.That(_adventures.Adventures.Single().Details, Is.EqualTo(new AdventureDetails()));
            Assert.That(_adventures.Adventures.Single().UpdatedAt, Is.EqualTo(before));
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task UpdateDetails_ByAnyoneButTheGm_Is403_AndSavesNothing(WorldRole role)
    {
        var view = await CreateAsync();

        var result = await _sut.UpdateDetailsAsync(
            new UpdateAdventureDetailsCommand(WorldId, view.Adventure.Id, role, "OSE", null, null, null), CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_adventures.Adventures.Single().Details, Is.EqualTo(new AdventureDetails()));
    }

    [Test]
    public async Task UpdateDetails_InAnotherWorld_Is404()
    {
        var view = await CreateAsync();

        var result = await _sut.UpdateDetailsAsync(
            new UpdateAdventureDetailsCommand(Guid.NewGuid(), view.Adventure.Id, WorldRole.GM, "OSE", null, null, null),
            CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task TheBooklet_CarriesTheResolvedDetails_OnItsCover()
    {
        var view = await CreateAsync();
        await _sut.UpdateDetailsAsync(
            new UpdateAdventureDetailsCommand(WorldId, view.Adventure.Id, WorldRole.GM, null, "1st", "1–3", 2026), CancellationToken.None);

        var booklet = await _sut.GetBookletAsync(WorldId, view.Adventure.Id, WorldRole.GM, CancellationToken.None);

        Assert.That(booklet.IsSuccess, Is.True, booklet.Error?.Message);
        var cover = _renderer.Rendered.Single().Cover;
        Assert.Multiple(() =>
        {
            Assert.That(cover.SystemLine, Is.EqualTo("Shadowdark 1st"), "the world's game system");
            Assert.That(cover.AuthorLine, Is.EqualTo($"By {TestPrintDetails.Username}"));
            Assert.That(_renderer.Rendered.Single().Rights.Credits, Is.EqualTo("Nornis Press · 2026"));
            Assert.That(booklet.Value!.Pdf, Is.EqualTo(FakeBookletPdfRenderer.BytesFor(_renderer.Rendered.Single(), _renderer.Size)));
            Assert.That(booklet.Value.FileName, Is.EqualTo($"{view.Adventure.Title}.pdf"));
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task OnlyTheGm_GetsTheBooklet(WorldRole role)
    {
        var view = await CreateAsync();

        var booklet = await _sut.GetBookletAsync(WorldId, view.Adventure.Id, role, CancellationToken.None);

        Assert.That(booklet.Error?.StatusCode, Is.EqualTo(403));
        Assert.That(_renderer.Rendered, Is.Empty);
    }

    [Test]
    public async Task AnotherWorldsBooklet_IsNotFound()
    {
        var view = await CreateAsync();

        var booklet = await _sut.GetBookletAsync(Guid.NewGuid(), view.Adventure.Id, WorldRole.GM, CancellationToken.None);

        Assert.That(booklet.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(_renderer.Rendered, Is.Empty);
    }

    // ------------------------------------------------------------- Delete --

    [Test]
    public async Task Delete_LeavesItsExportsAndPrints_WithTheLinkCleared()
    {
        var view = await CreateAsync();
        var export = new Source { Id = Guid.NewGuid(), WorldId = WorldId, Type = SourceType.AdventureExport, AdventureId = view.Adventure.Id };
        var print = new LibraryDocument { Id = Guid.NewGuid(), WorldId = WorldId, AdventureId = view.Adventure.Id };
        _adventures.Sources.Add(export);
        _adventures.Documents.Add(print);

        var result = await _sut.DeleteAsync(new DeleteAdventureCommand(WorldId, view.Adventure.Id, WorldRole.GM), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_adventures.Adventures, Is.Empty);
        Assert.That(_adventures.Sections, Is.Empty);
        Assert.That(export.AdventureId, Is.Null);
        Assert.That(print.AdventureId, Is.Null);
    }

    // --------------------------------------------------------------- Undo --

    [Test]
    public async Task Undo_RestoresTheLatestRevision_AndDropsIt()
    {
        var view = await CreateAsync();
        var overview = SectionOf(view, AdventureSectionKind.Overview);
        var now = DateTimeOffset.UtcNow;
        _adventures.SeedRevision(Revision(overview.Id, "Older body", now.AddMinutes(-2)));
        _adventures.SeedRevision(Revision(overview.Id, "The body before the last draft", now.AddMinutes(-1)));

        var result = await _sut.UndoAsync(
            new UndoSectionCommand(WorldId, view.Adventure.Id, overview.Id, WorldRole.GM), CancellationToken.None);

        Assert.That(result.Value!.BodyMarkdown, Is.EqualTo("The body before the last draft"));
        Assert.That(_adventures.Sections.Single(s => s.Id == overview.Id).BodyMarkdown, Is.EqualTo("The body before the last draft"));
        Assert.That(_adventures.Revisions.Select(r => r.BodyMarkdown), Is.EqualTo(["Older body"]));
    }

    [Test]
    public async Task Undo_WithNothingToUndo_IsAConflict()
    {
        var view = await CreateAsync();

        var result = await _sut.UndoAsync(
            new UndoSectionCommand(WorldId, view.Adventure.Id, view.Adventure.Sections.First().Id, WorldRole.GM),
            CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("nothing_to_undo"));
    }

    [Test]
    public async Task Budget_IsTheMonthsPrepSpend()
    {
        _budget.PrepSpentThisMonthUsd = 3.25m;
        _budget.PrepExceeded = true;

        var result = await _sut.GetBudgetAsync(WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value, Is.EqualTo(new PrepBudgetStatus(3.25m, 10.00m, IsExceeded: true)));
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task Budget_IsForTheGmOnly(WorldRole role)
    {
        var result = await _sut.GetBudgetAsync(WorldId, role, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Get_ReportsHowManyRevisionsEachSectionCanUndo()
    {
        var view = await CreateAsync();
        var overview = SectionOf(view, AdventureSectionKind.Overview);
        _adventures.SeedRevision(Revision(overview.Id, "a", DateTimeOffset.UtcNow));
        _adventures.SeedRevision(Revision(overview.Id, "b", DateTimeOffset.UtcNow));

        var result = await _sut.GetAsync(WorldId, view.Adventure.Id, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.RevisionCounts[overview.Id], Is.EqualTo(2));
        Assert.That(result.Value.RevisionCounts, Has.Count.EqualTo(1));
    }

    private static AdventureSectionRevision Revision(Guid sectionId, string body, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        SectionId = sectionId,
        BodyMarkdown = body,
        Model = "gpt-5.4",
        CreatedAt = at,
    };
}
