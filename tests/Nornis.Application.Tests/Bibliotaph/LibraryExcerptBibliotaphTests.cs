using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Bibliotaph;

/// <summary>Excerpts from a Bibliotaph book's pages (feature 31, Requirement 7).</summary>
[TestFixture]
public class LibraryExcerptBibliotaphTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private InMemorySourceRepository _sources = null!;
    private FakeExtractionQueueClient _queue = null!;
    private FakeReferencePassageRetriever _retriever = null!;
    private IBibliotaphClient _client = null!;
    private IBibliotaphLibrarySync _sync = null!;
    private LibraryExcerptService _sut = null!;
    private LibraryDocument _book = null!;

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _sources = new InMemorySourceRepository();
        _queue = new FakeExtractionQueueClient();
        _retriever = new FakeReferencePassageRetriever();
        _client = Substitute.For<IBibliotaphClient>();
        _client.GetPagesAsync(default, default, default!, default, default, default).ReturnsForAnyArgs(call =>
        {
            var (from, to) = (call.ArgAt<int>(3), call.ArgAt<int>(4));
            return BibliotaphResult<BibliotaphPageTexts>.Success(new BibliotaphPageTexts(
                Enumerable.Range(from, to - from + 1).Select(i => new BibliotaphPageText(i, $"{i + 11}", $"Text of index {i}.")).ToList()));
        });
        var owners = Substitute.For<IBibliotaphConnectionResolver>();
        owners.ForWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(new BibliotaphWorldOwner(
            WorldId, Creator, "Dave", new BibliotaphConnection { UserId = Creator }));
        _sync = Substitute.For<IBibliotaphLibrarySync>();

        var options = Options.Create(new LibraryOptions());
        var blobs = new FakeBlobStorageService();
        var chunks = new InMemoryLibraryChunkRepository();
        var library = new LibraryService(_documents, chunks, blobs, new FakeLibraryIndexingQueueClient(),
            new InMemoryAdventureRepository(), TestPrintDetails.None, options, NullLogger<LibraryService>.Instance);
        var sourceService = new SourceService(_sources, new InMemoryWorldMemberRepository(), new InMemoryCampaignRepository(),
            _queue, new InMemoryReviewBatchRepository(), new InMemoryReviewProposalRepository(),
            new InMemorySourceAttachmentRepository(), blobs, NullLogger<SourceService>.Instance);
        var pages = new BibliotaphExcerptPages(owners, _client, _documents, _sync,
            Options.Create(new BibliotaphOptions()), NullLogger<BibliotaphExcerptPages>.Instance);
        _sut = new LibraryExcerptService(library, chunks, new InMemoryArtifactRepository(), _retriever, _sources, sourceService,
            pages, options, NullLogger<LibraryExcerptService>.Instance);

        _book = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Origin = LibraryDocumentOrigin.Bibliotaph,
            BibliotaphEntryId = "entry-1",
            BibliotaphDocumentId = "doc-1",
            Title = "Player's Guide",
            Kind = LibraryDocumentKind.Sourcebook,
            Status = LibraryDocumentStatus.Stored,
            Visibility = VisibilityScope.GMOnly,
            ContentType = "application/pdf",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _documents.Seed(_book);
    }

    [Test]
    public async Task A_page_range_is_read_0_based_and_filed_1_based()
    {
        var result = await _sut.FileAsync(File(pageFrom: 5, pageTo: 7), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var source = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(source.Title, Is.EqualTo("Player's Guide, pp. 5–7"));
            Assert.That(source.Body, Is.EqualTo(
                "Excerpt from “Player's Guide”, pp. 5–7.\n\nText of index 4.\n\nText of index 5.\n\nText of index 6."));
            Assert.That((source.LibraryPageFrom, source.LibraryPageTo), Is.EqualTo((5, 7)));
            Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
            Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
        });
        await _client.Received(1).GetPagesAsync(Creator, WorldId, "doc-1", 4, 6, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Ticked_pages_apart_are_read_as_separate_runs()
    {
        var result = await _sut.FileAsync(File(pages: [9, 3, 4]), CancellationToken.None);

        Assert.That(result.Value!.Title, Is.EqualTo("Player's Guide, pp. 3–9"));
        Assert.That(result.Value.Body, Does.EndWith("Text of index 2.\n\nText of index 3.\n\nText of index 8."));
        await _client.Received(1).GetPagesAsync(Creator, WorldId, "doc-1", 2, 3, Arg.Any<CancellationToken>());
        await _client.Received(1).GetPagesAsync(Creator, WorldId, "doc-1", 8, 8, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Thirteen_pages_are_refused_before_Bibliotaph_is_asked()
    {
        var result = await _sut.FileAsync(File(pageFrom: 1, pageTo: 13), CancellationToken.None);

        Assert.That((result.Error!.StatusCode, result.Error.Code), Is.EqualTo((400, "excerpt_too_large")));
        await _client.DidNotReceiveWithAnyArgs().GetPagesAsync(default, default, default!, default, default, default);
        Assert.That(_sources.Sources, Is.Empty);
    }

    [Test]
    public async Task A_book_kept_from_AI_is_a_403_and_nothing_is_filed()
    {
        _client.GetPagesAsync(default, default, default!, default, default, default)
            .ReturnsForAnyArgs(BibliotaphResult<BibliotaphPageTexts>.Fail(BibliotaphFailure.KeptFromAi));

        var result = await _sut.FileAsync(File(pageFrom: 1, pageTo: 2), CancellationToken.None);

        Assert.That((result.Error!.StatusCode, result.Error.Code), Is.EqualTo((403, "kept_from_ai")));
        Assert.That(_sources.Sources, Is.Empty);
        Assert.That(_queue.SentMessages, Is.Empty);
    }

    [Test]
    public async Task A_body_over_the_source_limit_is_refused()
    {
        _client.GetPagesAsync(default, default, default!, default, default, default).ReturnsForAnyArgs(
            BibliotaphResult<BibliotaphPageTexts>.Success(new BibliotaphPageTexts(
                [new BibliotaphPageText(0, null, new string('x', 60_000)), new BibliotaphPageText(1, null, new string('y', 60_000))])));

        var result = await _sut.FileAsync(File(pageFrom: 1, pageTo: 2), CancellationToken.None);

        Assert.That((result.Error!.StatusCode, result.Error.Code), Is.EqualTo((400, "excerpt_too_large")));
        Assert.That(_sources.Sources, Is.Empty);
    }

    [Test]
    public async Task A_player_can_neither_read_pages_nor_file_them()
    {
        var read = await _sut.ReadPagesAsync(WorldId, WorldRole.Player, _book.Id, 1, 2, CancellationToken.None);
        var filed = await _sut.FileAsync(File(pageFrom: 1, pageTo: 2) with { ActingUserRole = WorldRole.Player }, CancellationToken.None);

        Assert.That(new[] { read.Error!.StatusCode, filed.Error!.StatusCode }, Is.All.EqualTo(403));
        await _client.DidNotReceiveWithAnyArgs().GetPagesAsync(default, default, default!, default, default, default);
    }

    [Test]
    public async Task The_GM_reads_pages_with_their_printed_labels()
    {
        var result = await _sut.ReadPagesAsync(WorldId, WorldRole.GM, _book.Id, 1, 2, CancellationToken.None);

        Assert.That(result.Value, Is.EqualTo([new LibraryPage(1, "11", "Text of index 0."), new LibraryPage(2, "12", "Text of index 1.")]));
    }

    [Test]
    public async Task Send_to_Nornis_finds_a_new_book_after_one_reconcile()
    {
        await _documents.DeleteAsync(_book.Id);
        _sync.ReconcileAsync(WorldId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _documents.Seed(_book);
            return new BibliotaphShelf(BibliotaphShelfStates.Ok, "Dave");
        });

        var found = await _sut.FindBibliotaphDocumentAsync(WorldId, WorldRole.GM, "doc-1", CancellationToken.None);
        var missing = await _sut.FindBibliotaphDocumentAsync(WorldId, WorldRole.GM, "doc-2", CancellationToken.None);

        Assert.That(found.Value!.Id, Is.EqualTo(_book.Id));
        Assert.That(missing.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Search_offers_Bibliotaph_pages_by_page_number()
    {
        _retriever.Passages.Add(new KnowledgePassage
        {
            ChunkId = null,
            DocumentId = _book.Id,
            DocumentTitle = _book.Title,
            Page = 5,
            PageLabel = "15",
            Text = "Thistlehold sits on the river.",
            ReferenceId = $"passage:{_book.Id:N}-4",
        });

        var result = await _sut.SearchAsync(
            new SearchLibraryExcerptsCommand(WorldId, Creator, WorldRole.GM, null, "Thistlehold"), CancellationToken.None);

        Assert.That(result.Value!.Single(), Is.EqualTo(
            new LibraryExcerptCandidate(null, _book.Id, "Player's Guide", 5, "Thistlehold sits on the river.", "15")));
        Assert.That(_retriever.LastMode, Is.EqualTo(ReferenceSearchMode.Search));
    }

    private FileLibraryExcerptCommand File(int? pageFrom = null, int? pageTo = null, IReadOnlyList<int>? pages = null) =>
        new(WorldId, Creator, WorldRole.GM, _book.Id, [], pageFrom, pageTo, null, pages);
}
