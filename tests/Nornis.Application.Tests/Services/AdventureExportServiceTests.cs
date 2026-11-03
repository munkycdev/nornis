using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Export to Nornis (feature 29, Requirement 8): a new GM-only AdventureExport source in the
/// Adventure's campaign, recording its Adventure, queued once through SourceService's one path.
/// </summary>
[TestFixture]
public class AdventureExportServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    private InMemoryAdventureRepository _adventures = null!;
    private InMemorySourceRepository _sources = null!;
    private FakeExtractionQueueClient _queue = null!;
    private AdventureService _adventureService = null!;
    private AdventureExportService _sut = null!;
    private Guid _campaignId;

    [SetUp]
    public async Task SetUp()
    {
        _adventures = new InMemoryAdventureRepository();
        _sources = new InMemorySourceRepository();
        _queue = new FakeExtractionQueueClient();
        var campaigns = new InMemoryCampaignRepository();
        _campaignId = Guid.NewGuid();
        await campaigns.CreateAsync(new Campaign
        {
            Id = _campaignId,
            WorldId = WorldId,
            Name = "Missing Caravan Arc",
            CreatedByUserId = GmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var sourceService = new SourceService(_sources, new InMemoryWorldMemberRepository(), campaigns,
            _queue, new InMemoryReviewBatchRepository(), new InMemoryReviewProposalRepository(),
            new InMemorySourceAttachmentRepository(), new FakeBlobStorageService(), NullLogger<SourceService>.Instance);
        _adventureService = new AdventureService(_adventures, campaigns, new FakeSlugResolver((_, _) => null),
            TestBooklets.Printer(TestPrintDetails.For(WorldId, GmId)), Options.Create(new PrepOptions()));
        var worlds = new InMemoryWorldRepository();
        await worlds.CreateAsync(new World
        {
            Id = WorldId,
            Name = "Black Harbor",
            GameSystem = "Fate Accelerated",
            CreatedByUserId = GmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _sut = new AdventureExportService(_adventures, _sources, sourceService, worlds, NullLogger<AdventureExportService>.Instance);
    }

    private async Task<Adventure> CreateAsync()
    {
        var created = await _adventureService.CreateAsync(
            new CreateAdventureCommand(WorldId, GmId, WorldRole.GM, "The Silver Key", "A heist.", _campaignId), CancellationToken.None);
        var adventure = created.Value!.Adventure;
        var overview = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Overview);
        await _adventureService.SaveSectionAsync(new SaveSectionCommand(WorldId, adventure.Id, overview.Id, WorldRole.GM,
            overview.Kind, overview.Title, "Captain Voss secretly serves the Shadow Guild.", overview.UpdatedAt), CancellationToken.None);
        return (await _adventures.GetWithSectionsAsync(adventure.Id))!;
    }

    private static ExportAdventureCommand Command(Adventure adventure, Guid? chapterId = null, WorldRole role = WorldRole.GM) =>
        new(WorldId, adventure.Id, chapterId, GmId, role);

    [Test]
    public async Task Export_FilesAGmOnlyAdventureExport_InTheCampaign_AndQueuesItOnce()
    {
        var adventure = await CreateAsync();

        var result = await _sut.ExportAsync(Command(adventure), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var source = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(source.Type, Is.EqualTo(SourceType.AdventureExport));
            Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
            Assert.That(source.CampaignId, Is.EqualTo(_campaignId));
            Assert.That(source.AdventureId, Is.EqualTo(adventure.Id));
            Assert.That(source.OccurredAt, Is.Null);
            Assert.That(source.ExtractionEnabled, Is.True);
            Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
            Assert.That(source.Title, Is.EqualTo("The Silver Key (prep)"));
            Assert.That(source.Body, Does.StartWith("Adventure \"The Silver Key\", exported "));
            Assert.That(source.Body, Does.Contain("Captain Voss secretly serves the Shadow Guild."));
            Assert.That(_queue.SentMessages, Has.Count.EqualTo(1));
            Assert.That(_queue.SentMessages[0].SourceId, Is.EqualTo(source.Id));
        });
    }

    [Test]
    public async Task ANamedBlock_IsFiledAsOneLine_NamingTheAdventuresSystem_AndAnUnnamedOneAsItsFence()
    {
        var adventure = await CreateAsync();
        var overview = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Overview);
        _adventures.Adventures.Single().Details = new AdventureDetails { GameSystem = "Shadowdark", Edition = "1st" };
        _adventures.Sections.Single(s => s.Id == overview.Id).BodyMarkdown =
            "Voss waits.\n\n```statblock\n# Captain Voss\nAC: 15\n```\n\n```item\nWeight: 1 lb\n```";

        var result = await _sut.ExportAsync(Command(adventure), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Body, Does.Contain("Voss waits.\n\nStat block for \"Captain Voss\" (Shadowdark 1st): AC: 15.\n\n```item"));
            Assert.That(result.Value.Body, Does.Not.Contain("```statblock"));
            Assert.That(result.Value.Body, Does.Contain("```item\nWeight: 1 lb\n```"), "an unnamed block files as its lines");
        });
    }

    [Test]
    public async Task WithNoSystemOfItsOwn_TheAdventureNamesTheWorlds()
    {
        var adventure = await CreateAsync();
        var overview = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Overview);
        _adventures.Sections.Single(s => s.Id == overview.Id).BodyMarkdown = "```statblock\n# Voss\nAC: 15\n```";

        var result = await _sut.ExportAsync(Command(adventure), CancellationToken.None);

        Assert.That(result.Value!.Body, Does.Contain("Stat block for \"Voss\" (Fate Accelerated): AC: 15."));
    }

    [Test]
    public async Task BlockLinesThatWouldOverflowASource_FallBackToTheBlocksOwnLines()
    {
        var adventure = await CreateAsync();
        var overview = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Overview);
        var tiny = "```statblock\n# A\nB: 1\n```\n\n";
        _adventures.Sections.Single(s => s.Id == overview.Id).BodyMarkdown =
            string.Concat(Enumerable.Repeat(tiny, 90_000 / tiny.Length));

        var result = await _sut.ExportAsync(Command(adventure), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Body!.Length, Is.LessThanOrEqualTo(SourceService.MaxBodyChars));
            Assert.That(result.Value.Body, Does.Contain("```statblock"));
            Assert.That(result.Value.Body, Does.Not.Contain("Stat block for"));
        });
    }

    [Test]
    public async Task EachExport_IsANewSource()
    {
        var adventure = await CreateAsync();

        var first = await _sut.ExportAsync(Command(adventure), CancellationToken.None);
        var second = await _sut.ExportAsync(Command(adventure), CancellationToken.None);

        Assert.That(second.Value!.Id, Is.Not.EqualTo(first.Value!.Id));
    }

    [Test]
    public async Task AChapterExport_HoldsOnlyThatChapter()
    {
        var adventure = await CreateAsync();
        var chapter = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Chapter);
        await _adventureService.SaveSectionAsync(new SaveSectionCommand(WorldId, adventure.Id, chapter.Id, WorldRole.GM,
            chapter.Kind, chapter.Title, "The harbor at dusk.", chapter.UpdatedAt), CancellationToken.None);

        var result = await _sut.ExportAsync(Command(adventure, chapter.Id), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Title, Is.EqualTo("The Silver Key: Chapter 1 (prep)"));
            Assert.That(result.Value.Body, Does.Contain("The harbor at dusk."));
            Assert.That(result.Value.Body, Does.Not.Contain("Shadow Guild"), "the Overview is before the chapter");
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task OnlyTheGm_CanExport(WorldRole role)
    {
        var adventure = await CreateAsync();

        var result = await _sut.ExportAsync(Command(adventure, role: role), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(403));
        Assert.That(_queue.SentMessages, Is.Empty);
    }

    [Test]
    public async Task AnUnwrittenAdventure_HasNothingToExport()
    {
        var created = await _adventureService.CreateAsync(
            new CreateAdventureCommand(WorldId, GmId, WorldRole.GM, "Blank", null, null), CancellationToken.None);

        var result = await _sut.ExportAsync(Command(created.Value!.Adventure), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("nothing_to_export"));
    }

    [Test]
    public async Task AnotherWorld_OrANonChapter_IsNotFound()
    {
        var adventure = await CreateAsync();
        var scene = adventure.Sections.First(s => s.Kind == AdventureSectionKind.Scene);

        var elsewhere = await _sut.ExportAsync(Command(adventure) with { WorldId = Guid.NewGuid() }, CancellationToken.None);
        var notAChapter = await _sut.ExportAsync(Command(adventure, scene.Id), CancellationToken.None);

        Assert.That(elsewhere.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(notAChapter.Error?.StatusCode, Is.EqualTo(404));
    }
}
