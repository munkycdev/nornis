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
/// File to Library for a Prep print (feature 29, Requirement 7.2–7.5; feature 34): one call renders
/// the booklet and files it as a GM-only, dated Handout that records its Adventure and the details
/// it was printed with. Each print is a new document that becomes the Adventure's last print, and
/// an upload can no longer file one.
/// </summary>
[TestFixture]
public class LibraryPrintTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private FakeBlobStorageService _blobs = null!;
    private InMemoryAdventureRepository _adventures = null!;
    private FakeBookletPdfRenderer _renderer = null!;
    private LibraryOptions _options = null!;
    private LibraryService _sut = null!;
    private Adventure _adventure = null!;

    [SetUp]
    public async Task SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _blobs = new FakeBlobStorageService();
        _adventures = new InMemoryAdventureRepository();
        _renderer = new FakeBookletPdfRenderer();
        _options = new LibraryOptions();
        _sut = new LibraryService(_documents, new InMemoryLibraryChunkRepository(), _blobs,
            new FakeLibraryIndexingQueueClient(), _adventures,
            TestBooklets.Printer(TestPrintDetails.For(WorldId, GmId, "Shadowdark"), _renderer), Options.Create(_options),
            NullLogger<LibraryService>.Instance);
        _adventure = await SeedAdventureAsync(WorldId, "The Silver Key");
    }

    private async Task<Adventure> SeedAdventureAsync(Guid worldId, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return await _adventures.CreateAsync(new Adventure
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Title = title,
            CreatedByUserId = GmId,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static FilePrintCommand Print(Guid adventureId, WorldRole role = WorldRole.GM, Guid? worldId = null) =>
        new(worldId ?? WorldId, GmId, role, adventureId);

    private static RequestLibraryUploadCommand Upload(Guid? adventureId = null) =>
        new(WorldId, GmId, WorldRole.GM, "Sourcebook", "book.pdf", "application/pdf", 2048,
            LibraryDocumentKind.Sourcebook, VisibilityScope.PartyVisible, adventureId);

    private async Task<LibraryDocument> FileAsync()
    {
        var filed = await _sut.FilePrintAsync(Print(_adventure.Id), CancellationToken.None);
        Assert.That(filed.IsSuccess, Is.True, filed.Error?.Message);
        return filed.Value!;
    }

    [Test]
    public async Task APrint_IsAGmOnlyDatedHandout_ThatRecordsItsAdventure()
    {
        var document = await FileAsync();

        Assert.Multiple(() =>
        {
            Assert.That(document.Kind, Is.EqualTo(LibraryDocumentKind.Handout));
            Assert.That(document.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
            Assert.That(document.Title, Is.EqualTo($"The Silver Key, printed {DateTimeOffset.UtcNow:yyyy-MM-dd}"));
            Assert.That(document.AdventureId, Is.EqualTo(_adventure.Id));
            Assert.That(document.FileName, Is.EqualTo("The Silver Key.pdf"));
            Assert.That(document.ContentType, Is.EqualTo(LibraryService.PdfContentType));
            Assert.That(document.UploadedByUserId, Is.EqualTo(GmId));
        });
    }

    /// <summary>The blob holds the booklet the printer rendered, and the row is stored, not pending.</summary>
    [Test]
    public async Task ThePrint_StoresTheRenderedBooklet_AndIsNoLongerPending()
    {
        var document = await FileAsync();

        var stored = _documents.Documents.Single(d => d.Id == document.Id);
        var booklet = _renderer.Rendered.Single();
        Assert.Multiple(() =>
        {
            Assert.That(booklet.Cover.Title, Is.EqualTo("The Silver Key"));
            Assert.That(_blobs.Blobs[stored.BlobPath].Content, Is.EqualTo(FakeBookletPdfRenderer.BytesFor(booklet, _renderer.Size)));
            Assert.That(_blobs.Blobs[stored.BlobPath].ContentType, Is.EqualTo(LibraryService.PdfContentType));
            Assert.That(stored.SizeBytes, Is.EqualTo(_renderer.Size));
            Assert.That(stored.Status, Is.Not.EqualTo(LibraryDocumentStatus.PendingUpload));
            Assert.That(stored.AdventureId, Is.EqualTo(_adventure.Id));
            Assert.That(stored.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task OnlyTheGm_CanFileAPrint(WorldRole role)
    {
        var result = await _sut.FilePrintAsync(Print(_adventure.Id, role), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(403));
        Assert.That(_documents.Documents, Is.Empty);
        Assert.That(_renderer.Rendered, Is.Empty);
    }

    [Test]
    public async Task AnotherWorldsAdventure_IsNotFound()
    {
        var elsewhere = await SeedAdventureAsync(Guid.NewGuid(), "Elsewhere");

        var result = await _sut.FilePrintAsync(Print(elsewhere.Id), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(_documents.Documents, Is.Empty);
    }

    [Test]
    public async Task ARenderThatFails_StoresNothing()
    {
        _renderer.Throw = true;

        var result = await _sut.FilePrintAsync(Print(_adventure.Id), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("print_failed"));
        Assert.That(_documents.Documents, Is.Empty);
        Assert.That(_blobs.Blobs, Is.Empty);
    }

    [Test]
    public async Task ABookletOverTheUploadLimit_StoresNothing()
    {
        _options.MaxUploadSizeBytes = 1_000;
        _renderer.Size = 1_001;

        var result = await _sut.FilePrintAsync(Print(_adventure.Id), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("upload_too_large"));
        Assert.That(_documents.Documents, Is.Empty);
        Assert.That(_blobs.Blobs, Is.Empty);
    }

    [Test]
    public async Task ABookletAtTheUploadLimit_IsFiled()
    {
        _options.MaxUploadSizeBytes = 1_000;
        _renderer.Size = 1_000;

        var result = await _sut.FilePrintAsync(Print(_adventure.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
    }

    [Test]
    public async Task EachPrint_IsANewDocument_AndTheNewestIsTheLastPrint()
    {
        var first = await FileAsync();
        var second = await FileAsync();

        var adventure = await _adventures.GetWithSectionsAsync(_adventure.Id);
        Assert.Multiple(() =>
        {
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
            Assert.That(_documents.Documents.Select(d => d.Id), Is.SupersetOf([first.Id, second.Id]));
            Assert.That(adventure!.LastPrintedDocumentId, Is.EqualTo(second.Id));
        });
    }

    /// <summary>Feature 34, Requirement 4.7: one path files prints, so an upload that names an Adventure is refused.</summary>
    [Test]
    public async Task AnUploadThatNamesAnAdventure_IsRefused()
    {
        var result = await _sut.RequestUploadAsync(Upload(_adventure.Id), CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("prints_are_filed_from_prep"));
        Assert.That(_documents.Documents, Is.Empty);
    }

    [Test]
    public async Task AnOrdinaryUpload_TouchesNoAdventure()
    {
        var ticket = (await _sut.RequestUploadAsync(Upload(), CancellationToken.None)).Value!;
        _blobs.Blobs[ticket.Document.BlobPath] = (new byte[2048], "application/pdf");
        await _sut.ConfirmUploadAsync(ticket.Document.Id, WorldId, GmId, CancellationToken.None);

        Assert.That(ticket.Document.AdventureId, Is.Null);
        Assert.That(ticket.Document.Kind, Is.EqualTo(LibraryDocumentKind.Sourcebook));
        Assert.That((await _adventures.GetWithSectionsAsync(_adventure.Id))!.LastPrintedDocumentId, Is.Null);
    }

    // ------------------------------------------------------- Print details (feature 32) --

    [Test]
    public async Task APrint_KeepsItsDetails_ResolvedAsTheyStoodWhenFiled()
    {
        await SetDetailsAsync(new AdventureDetails { Edition = "1st", Levels = "1–3", Year = 2026 });

        var filed = await FileAsync();

        var stored = _documents.Documents.Single(d => d.Id == filed.Id);
        Assert.That(stored.PrintDetails, Is.EqualTo(new PrintDetails
        {
            Title = "The Silver Key",
            GameSystem = "Shadowdark",
            Edition = "1st",
            Type = PrintDetails.NornisType,
            Levels = "1–3",
            Publisher = PrintDetails.NornisPublisher,
            Author = TestPrintDetails.Username,
            Year = 2026,
            AiUse = AiUseKind.None,
        }));
    }

    [Test]
    public async Task ChangingTheAdventureAfterwards_LeavesAnEarlierPrintsDetails()
    {
        var filed = await FileAsync();
        var asFiled = _documents.Documents.Single(d => d.Id == filed.Id).PrintDetails;

        var renamed = (await _adventures.GetWithSectionsAsync(_adventure.Id))!;
        renamed.Title = "The Golden Key";
        renamed.Details = new AdventureDetails { GameSystem = "OSE", Year = 2027 };
        await _adventures.UpdateAsync(renamed);
        var second = await FileAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_documents.Documents.Single(d => d.Id == filed.Id).PrintDetails, Is.EqualTo(asFiled));
            Assert.That(asFiled!.Title, Is.EqualTo("The Silver Key"));
            Assert.That(_documents.Documents.Single(d => d.Id == second.Id).PrintDetails!.Title, Is.EqualTo("The Golden Key"));
            Assert.That(_documents.Documents.Single(d => d.Id == second.Id).PrintDetails!.GameSystem, Is.EqualTo("OSE"));
        });
    }

    [Test]
    public async Task AnOrdinaryUpload_HasNoPrintDetails()
    {
        var ticket = (await _sut.RequestUploadAsync(Upload(), CancellationToken.None)).Value!;

        Assert.That(ticket.Document.PrintDetails, Is.Null);
    }

    private async Task SetDetailsAsync(AdventureDetails details)
    {
        var adventure = (await _adventures.GetWithSectionsAsync(_adventure.Id))!;
        adventure.Details = details;
        await _adventures.UpdateAsync(adventure);
    }

    [Test]
    public void PrintTitle_FitsTheTitleBound_AndKeepsTheDate()
    {
        var title = LibraryService.PrintTitle(new string('x', 200), new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));

        Assert.That(title, Has.Length.LessThanOrEqualTo(200));
        Assert.That(title, Does.EndWith(", printed 2026-11-14"));
    }
}
