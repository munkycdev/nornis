using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Design mode's storage (feature 37): the draft and its Undo, the table brief, and the source
/// books, each GM-only and each refusing what it must.
/// </summary>
[TestFixture]
public class AdventureDesignServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    private InMemoryAdventureRepository _adventures = null!;
    private InMemoryLibraryDocumentRepository _documents = null!;
    private AdventureService _views = null!;
    private AdventureDesignService _sut = null!;
    private Guid _adventureId;

    [SetUp]
    public async Task SetUp()
    {
        _adventures = new InMemoryAdventureRepository();
        _documents = new InMemoryLibraryDocumentRepository();
        var slugs = new FakeSlugResolver((_, _) => null);
        _views = new AdventureService(_adventures, new InMemoryCampaignRepository(), slugs,
            TestBooklets.Printer(TestPrintDetails.For(WorldId, GmUserId, "Shadowdark"), new FakeBookletPdfRenderer()),
            Options.Create(new PrepOptions()));
        _sut = new AdventureDesignService(_adventures, _documents, _views);

        var created = await _views.CreateAsync(
            new CreateAdventureCommand(WorldId, GmUserId, WorldRole.GM, "The Slurping Spa", null, null), default);
        _adventureId = created.Value!.Adventure.Id;
    }

    private static IEnumerable<TestCaseData> EveryOperation()
    {
        var ops = new (string Name, Func<AdventureDesignService, Guid, WorldRole, Task<AppError?>> Run)[]
        {
            ("SaveDraft", async (s, a, r) => (await s.SaveDraftAsync(new(WorldId, a, r, "# Draft", null), default)).Error),
            ("UndoDraft", async (s, a, r) => (await s.UndoDraftAsync(new(WorldId, a, r), default)).Error),
            ("SetTableBrief", async (s, a, r) => (await s.SetTableBriefAsync(new(WorldId, a, r, "Four players"), default)).Error),
            ("SetSourceBooks", async (s, a, r) => (await s.SetSourceBooksAsync(new(WorldId, a, r, []), default)).Error),
        };
        foreach (var (name, run) in ops)
        {
            foreach (var role in new[] { WorldRole.Player, WorldRole.Observer })
            {
                yield return new TestCaseData(run, role).SetName($"{name}_IsRefused_For{role}");
            }
        }
    }

    [TestCaseSource(nameof(EveryOperation))]
    public async Task EveryOperation_IsGmOnly(Func<AdventureDesignService, Guid, WorldRole, Task<AppError?>> run, WorldRole role)
    {
        var error = await run(_sut, _adventureId, role);

        Assert.That(error?.StatusCode, Is.EqualTo(403));
        Assert.That(_adventures.Adventures.Single().DraftMarkdown, Is.Null);
    }

    [Test]
    public async Task AFirstSave_StartsTheDraft_AndAnotherAdventuresIdIsNotFound()
    {
        var saved = await _sut.SaveDraftAsync(new(WorldId, _adventureId, WorldRole.GM, "# The Spa", null), default);
        var stranger = await _sut.SaveDraftAsync(new(Guid.NewGuid(), _adventureId, WorldRole.GM, "x", null), default);

        Assert.Multiple(() =>
        {
            Assert.That(saved.Value!.Markdown, Is.EqualTo("# The Spa"));
            Assert.That(saved.Value.UpdatedAt, Is.Not.Null);
            Assert.That(_adventures.Adventures.Single().DraftMarkdown, Is.EqualTo("# The Spa"));
            Assert.That(stranger.Error?.StatusCode, Is.EqualTo(404));
        });
    }

    [Test]
    public async Task ASaveBasedOnAnOlderStamp_IsRefused_AndChangesNothing()
    {
        var first = await _sut.SaveDraftAsync(new(WorldId, _adventureId, WorldRole.GM, "one", null), default);
        await _sut.SaveDraftAsync(new(WorldId, _adventureId, WorldRole.GM, "two", first.Value!.UpdatedAt), default);

        var stale = await _sut.SaveDraftAsync(new(WorldId, _adventureId, WorldRole.GM, "three", first.Value.UpdatedAt), default);

        Assert.Multiple(() =>
        {
            Assert.That(stale.Error?.Code, Is.EqualTo("draft_changed"));
            Assert.That(_adventures.Adventures.Single().DraftMarkdown, Is.EqualTo("two"));
        });
    }

    [Test]
    public async Task ADraftOverTheCap_IsRefused()
    {
        var result = await _sut.SaveDraftAsync(
            new(WorldId, _adventureId, WorldRole.GM, new string('a', Adventure.MaxDraftChars + 1), null), default);

        Assert.That(result.Error?.Code, Is.EqualTo("prep_too_large"));
        Assert.That(_adventures.Adventures.Single().DraftMarkdown, Is.Null);
    }

    [Test]
    public async Task TheGmsOwnTyping_KeepsNoRevision_SoThereIsNothingToUndo()
    {
        var first = await _sut.SaveDraftAsync(new(WorldId, _adventureId, WorldRole.GM, "one", null), default);
        await _sut.SaveDraftAsync(new(WorldId, _adventureId, WorldRole.GM, "two", first.Value!.UpdatedAt), default);

        var undo = await _sut.UndoDraftAsync(new(WorldId, _adventureId, WorldRole.GM), default);

        Assert.That(undo.Error?.Code, Is.EqualTo("nothing_to_undo"));
    }

    [Test]
    public async Task Undo_RestoresTheDraftBeforeAChangeThatKeptARevision_AndDropsIt()
    {
        var first = await _sut.SaveDraftAsync(new(WorldId, _adventureId, WorldRole.GM, "mine", null), default);
        await _adventures.TrySaveDraftAsync(_adventureId, "the Loremaster's",
            new AdventureDraftRevision { Id = Guid.NewGuid(), Reason = AdventureDraftChange.Edit, CreatedAt = DateTimeOffset.UtcNow },
            maxRevisions: 20, first.Value!.UpdatedAt, DateTimeOffset.UtcNow.AddSeconds(1));

        var undo = await _sut.UndoDraftAsync(new(WorldId, _adventureId, WorldRole.GM), default);

        Assert.Multiple(() =>
        {
            Assert.That(undo.Value!.Markdown, Is.EqualTo("mine"));
            Assert.That(undo.Value.RevisionCount, Is.Zero);
            Assert.That(_adventures.Adventures.Single().DraftMarkdown, Is.EqualTo("mine"));
        });
    }

    [Test]
    public async Task TheBrief_IsTrimmed_AndBlankClearsIt()
    {
        var set = await _sut.SetTableBriefAsync(new(WorldId, _adventureId, WorldRole.GM, "  Four players, level 3, two hours.  "), default);
        Assert.That(set.Value!.Adventure.TableBrief, Is.EqualTo("Four players, level 3, two hours."));

        var cleared = await _sut.SetTableBriefAsync(new(WorldId, _adventureId, WorldRole.GM, "   "), default);
        Assert.That(cleared.Value!.Adventure.TableBrief, Is.Null);
    }

    [Test]
    public async Task ABriefOverTheCap_IsRefused()
    {
        var result = await _sut.SetTableBriefAsync(
            new(WorldId, _adventureId, WorldRole.GM, new string('b', Adventure.MaxTableBriefChars + 1)), default);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task SourceBooks_AreTheWorldsBooks_UpToFive()
    {
        var books = Enumerable.Range(1, 6).Select(i => Book($"Book {i}")).ToArray();
        Seed(books);

        var five = await _sut.SetSourceBooksAsync(new(WorldId, _adventureId, WorldRole.GM, books.Take(5).Select(b => b.Id).ToList()), default);
        var six = await _sut.SetSourceBooksAsync(new(WorldId, _adventureId, WorldRole.GM, books.Select(b => b.Id).ToList()), default);

        Assert.Multiple(() =>
        {
            Assert.That(five.Value!.Adventure.SourceBooks, Has.Count.EqualTo(5));
            Assert.That(six.Error?.Code, Is.EqualTo("too_many_source_books"));
        });
    }

    [Test]
    public async Task ASourceBook_MustBeABook_InThisWorld_ThatHasntLeft()
    {
        var map = Book("Harbor map");
        map.Kind = LibraryDocumentKind.Map;
        var elsewhere = Book("Another world's book");
        elsewhere.WorldId = Guid.NewGuid();
        var removed = Book("Taken out of Bibliotaph");
        removed.RemovedAt = DateTimeOffset.UtcNow;
        var print = Book("The Slurping Spa (print)");
        print.AdventureId = _adventureId;
        Seed(map, elsewhere, removed, print);

        async Task<string?> Try(LibraryDocument d) =>
            (await _sut.SetSourceBooksAsync(new(WorldId, _adventureId, WorldRole.GM, [d.Id]), default)).Error?.Code;

        var codes = new[] { await Try(map), await Try(elsewhere), await Try(removed), await Try(print) };

        Assert.That(codes, Is.EqualTo(new[] { "not_a_book", "document_not_found", "document_not_found", "not_a_book" }));
    }

    [Test]
    public async Task NoSourceBooks_ClearsTheChoice()
    {
        var book = Book("Player's Guide");
        Seed(book);
        await _sut.SetSourceBooksAsync(new(WorldId, _adventureId, WorldRole.GM, [book.Id]), default);

        var cleared = await _sut.SetSourceBooksAsync(new(WorldId, _adventureId, WorldRole.GM, []), default);

        Assert.That(cleared.Value!.Adventure.SourceBooks, Is.Empty);
    }

    private void Seed(params LibraryDocument[] documents)
    {
        _documents.Seed(documents);
        _adventures.Documents.AddRange(documents);
    }

    private static LibraryDocument Book(string title) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Title = title,
        Kind = LibraryDocumentKind.Sourcebook,
        Status = LibraryDocumentStatus.Stored,
    };
}
