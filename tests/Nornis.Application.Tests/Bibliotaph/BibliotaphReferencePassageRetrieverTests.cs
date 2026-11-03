using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Knowledge;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Bibliotaph;

/// <summary>What a question may search in a world's Bibliotaph (feature 31, Requirement 4).</summary>
[TestFixture]
public class BibliotaphReferencePassageRetrieverTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private FakeReferencePassageRetriever _uploads = null!;
    private IBibliotaphClient _client = null!;
    private IBibliotaphConnectionResolver _owners = null!;
    private IBibliotaphLibrarySync _sync = null!;
    private BibliotaphReferencePassageRetriever _sut = null!;
    private LibraryDocument _shared = null!;
    private LibraryDocument _secret = null!;

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _shared = Row("e-shared", "Player's Guide", VisibilityScope.PartyVisible);
        _secret = Row("e-secret", "Adventure Module", VisibilityScope.GMOnly);
        _documents.Seed(_shared, _secret);
        _uploads = new FakeReferencePassageRetriever();
        _client = Substitute.For<IBibliotaphClient>();
        _owners = Substitute.For<IBibliotaphConnectionResolver>();
        _owners.ForWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(new BibliotaphWorldOwner(
            WorldId, Creator, "Dave", new BibliotaphConnection { UserId = Creator }));
        _sync = Substitute.For<IBibliotaphLibrarySync>();
        _sut = new BibliotaphReferencePassageRetriever(
            _uploads, _owners, _client, _documents, _sync, NullLogger<BibliotaphReferencePassageRetriever>.Instance);
    }

    [Test]
    public async Task A_players_question_names_only_the_shared_books()
    {
        BibliotaphPassagesRequest? sent = null;
        _client.FindPassagesAsync(Creator, WorldId, Arg.Do<BibliotaphPassagesRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(Pages(Page("e-shared", 4, "5")));

        var found = await _sut.RetrieveAsync("how do I level up?", WorldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(sent!.EntryIds, Is.EqualTo(["e-shared"]));
        var passage = found.Passages.Single();
        Assert.Multiple(() =>
        {
            Assert.That(passage.DocumentId, Is.EqualTo(_shared.Id));
            Assert.That((passage.Page, passage.PageLabel, passage.PageShown), Is.EqualTo((5, "5", "5")));
            Assert.That(passage.ChunkId, Is.Null);
        });
    }

    [Test]
    public async Task A_page_from_a_GMOnly_book_never_reaches_a_player_even_if_Bibliotaph_sends_it()
    {
        // Sabotage: Bibliotaph ignores entryIds and answers from the whole collection.
        _client.FindPassagesAsync(default, default, default!, default)
            .ReturnsForAnyArgs(Pages(Page("e-secret", 0, null), Page("e-shared", 1, null)));

        var found = await _sut.RetrieveAsync("the villain?", WorldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(found.Passages.Select(p => p.DocumentId), Is.EqualTo([_shared.Id]));
    }

    [Test]
    public async Task With_nothing_shared_a_player_makes_no_call()
    {
        _shared.Visibility = VisibilityScope.GMOnly;

        var found = await _sut.RetrieveAsync("q", WorldId, Guid.NewGuid(), WorldRole.Observer, CancellationToken.None);

        Assert.That(found.Passages, Is.Empty);
        await _client.DidNotReceiveWithAnyArgs().FindPassagesAsync(default, default, default!, default);
    }

    [Test]
    public async Task An_adventures_source_books_ask_for_those_books_alone_including_GMOnly_ones()
    {
        BibliotaphPassagesRequest? sent = null;
        // Sabotage: Bibliotaph answers from a book that wasn't chosen too.
        _client.FindPassagesAsync(Creator, WorldId, Arg.Do<BibliotaphPassagesRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(Pages(Page("e-secret", 2, null), Page("e-shared", 3, null)));

        var found = await _sut.RetrieveFromDocumentsAsync("slimes", WorldId, Creator, [_secret.Id], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(sent!.EntryIds, Is.EqualTo(["e-secret"]));
            Assert.That(found.Passages.Select(p => p.DocumentId), Is.EqualTo([_secret.Id]));
        });
    }

    [Test]
    public async Task Source_books_that_left_the_collection_make_no_call()
    {
        _secret.RemovedAt = DateTimeOffset.UtcNow;

        var found = await _sut.RetrieveFromDocumentsAsync("slimes", WorldId, Creator, [_secret.Id], CancellationToken.None);

        Assert.That(found.Passages, Is.Empty);
        await _client.DidNotReceiveWithAnyArgs().FindPassagesAsync(default, default, default!, default);
    }

    [Test]
    public async Task A_GMs_question_searches_the_whole_collection()
    {
        BibliotaphPassagesRequest? sent = null;
        _client.FindPassagesAsync(Creator, WorldId, Arg.Do<BibliotaphPassagesRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(Pages(Page("e-secret", 9, "x")));

        var found = await _sut.RetrieveAsync("q", WorldId, Creator, WorldRole.GM, CancellationToken.None);

        Assert.That(sent!.EntryIds, Is.Null);
        Assert.That(sent.Mode, Is.EqualTo("ask"));
        Assert.That(found.Passages.Single().DocumentId, Is.EqualTo(_secret.Id));
    }

    [Test]
    public async Task Extraction_from_a_shared_source_never_searches_GMOnly_books()
    {
        BibliotaphPassagesRequest? sent = null;
        _client.FindPassagesAsync(Creator, WorldId, Arg.Do<BibliotaphPassagesRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(Pages());

        await _sut.RetrieveForScopesAsync("q", WorldId, [VisibilityScope.PartyVisible], Creator, ReferenceSearchMode.Ask, CancellationToken.None);

        Assert.That(sent!.EntryIds, Is.EqualTo(["e-shared"]));
        Assert.That(sent.Mode, Is.EqualTo(BibliotaphPassageModes.Ask));
    }

    [Test]
    public async Task Choosing_excerpts_searches_for_the_closest_pages()
    {
        BibliotaphPassagesRequest? sent = null;
        _client.FindPassagesAsync(Creator, WorldId, Arg.Do<BibliotaphPassagesRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(Pages());

        await _sut.RetrieveForScopesAsync("q", WorldId, [VisibilityScope.PartyVisible, VisibilityScope.GMOnly], Creator,
            ReferenceSearchMode.Search, CancellationToken.None);

        Assert.That(sent!.Mode, Is.EqualTo(BibliotaphPassageModes.Search));
    }

    [Test]
    public async Task A_failure_keeps_the_uploads_and_says_the_books_were_unavailable()
    {
        _uploads.Passages.Add(new KnowledgePassage
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            DocumentTitle = "Upload",
            Page = 1,
            Text = "t",
            ReferenceId = "passage:x",
        });
        _client.FindPassagesAsync(default, default, default!, default)
            .ReturnsForAnyArgs(BibliotaphResult<BibliotaphPassages>.Fail(BibliotaphFailure.Unreachable));

        var found = await _sut.RetrieveAsync("q", WorldId, Creator, WorldRole.GM, CancellationToken.None);

        Assert.That(found.Unavailable, Is.True);
        Assert.That(found.Passages.Single().DocumentTitle, Is.EqualTo("Upload"));
    }

    [Test]
    public async Task An_unconnected_world_is_uploads_only()
    {
        _owners.ForWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(new BibliotaphWorldOwner(WorldId, Creator, "", null));

        var found = await _sut.RetrieveAsync("q", WorldId, Creator, WorldRole.GM, CancellationToken.None);

        Assert.That(found.Unavailable, Is.False);
        await _client.DidNotReceiveWithAnyArgs().FindPassagesAsync(default, default, default!, default);
    }

    [Test]
    public async Task A_GMs_unknown_book_reconciles_once_and_is_then_quoted()
    {
        _client.FindPassagesAsync(default, default, default!, default).ReturnsForAnyArgs(Pages(Page("e-new", 0, null)));
        _sync.ReconcileAsync(WorldId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _documents.Seed(Row("e-new", "New Book", VisibilityScope.GMOnly));
            return new BibliotaphShelf(BibliotaphShelfStates.Ok, "Dave");
        });

        var found = await _sut.RetrieveAsync("q", WorldId, Creator, WorldRole.GM, CancellationToken.None);

        await _sync.Received(1).ReconcileAsync(WorldId, Arg.Any<CancellationToken>());
        Assert.That(found.Passages.Single().DocumentTitle, Is.EqualTo("New Book"));
    }

    private static BibliotaphResult<BibliotaphPassages> Pages(params BibliotaphPassage[] pages) =>
        BibliotaphResult<BibliotaphPassages>.Success(new BibliotaphPassages(true, false, pages));

    private static BibliotaphPassage Page(string entryId, int pageIndex, string? label) =>
        new(entryId, $"d-{entryId}", "title", pageIndex, label, $"text of {entryId}", 0.9);

    private static LibraryDocument Row(string entryId, string title, VisibilityScope visibility) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Origin = LibraryDocumentOrigin.Bibliotaph,
        BibliotaphEntryId = entryId,
        BibliotaphDocumentId = $"d-{entryId}",
        Title = title,
        RemoteTitle = title,
        Kind = LibraryDocumentKind.Sourcebook,
        Status = LibraryDocumentStatus.Stored,
        Visibility = visibility,
    };
}
