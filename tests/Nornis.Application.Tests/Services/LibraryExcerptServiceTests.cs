using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// A GM files pages of a Library document as an excerpt source. The excerpt inherits the
/// document's shelf, is composed in reading order, and reaches the queue through
/// SourceService's one path — so the enqueue-failure revert is inherited, not re-implemented.
/// </summary>
[TestFixture]
public class LibraryExcerptServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OtherWorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();
    private static readonly Guid PlayerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid ThistleholdId = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private InMemoryLibraryChunkRepository _chunks = null!;
    private InMemoryArtifactRepository _artifacts = null!;
    private FakeReferencePassageRetriever _retriever = null!;
    private InMemorySourceRepository _sources = null!;
    private FakeExtractionQueueClient _queue = null!;
    private LibraryExcerptService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _chunks = new InMemoryLibraryChunkRepository();
        _artifacts = new InMemoryArtifactRepository();
        _retriever = new FakeReferencePassageRetriever();
        _sources = new InMemorySourceRepository();
        _queue = new FakeExtractionQueueClient();

        var options = Options.Create(new LibraryOptions { MaxExcerptChunks = 3, OverlapChars = 4 });
        var blobs = new FakeBlobStorageService();
        var libraryService = new LibraryService(_documents, _chunks, blobs, new FakeLibraryIndexingQueueClient(),
            options, NullLogger<LibraryService>.Instance);
        var sourceService = new SourceService(_sources, new InMemoryWorldMemberRepository(), new InMemoryCampaignRepository(),
            _queue, new InMemoryReviewBatchRepository(), new InMemoryReviewProposalRepository(),
            new InMemorySourceAttachmentRepository(), blobs, NullLogger<SourceService>.Instance);

        _sut = new LibraryExcerptService(libraryService, _chunks, _artifacts, _retriever, _sources, sourceService,
            options, NullLogger<LibraryExcerptService>.Instance);

        _documents.Seed(Document(DocumentId, WorldId, VisibilityScope.GMOnly, LibraryDocumentStatus.Indexed));
        _artifacts.Seed(new Artifact
        {
            Id = ThistleholdId,
            WorldId = WorldId,
            Type = ArtifactType.Location,
            Name = "Thistlehold",
            Summary = "A river city the party passed through.",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static LibraryDocument Document(Guid id, Guid worldId, VisibilityScope visibility, LibraryDocumentStatus status) => new()
    {
        Id = id,
        WorldId = worldId,
        Title = "Player's Guide",
        FileName = "guide.pdf",
        ContentType = "application/pdf",
        Kind = LibraryDocumentKind.Sourcebook,
        Visibility = visibility,
        Status = status,
        UploadedByUserId = GmId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private LibraryChunkHit SeedChunk(int ord, int page, string text, Guid? documentId = null)
    {
        var hit = new LibraryChunkHit(Guid.NewGuid(), documentId ?? DocumentId, "Player's Guide", ord, page, text, 0d);
        _chunks.AllChunks.Add(hit);
        return hit;
    }

    private static SearchLibraryExcerptsCommand Search(WorldRole role = WorldRole.GM, Guid? artifactId = null, string? query = null) =>
        new(WorldId, role == WorldRole.GM ? GmId : PlayerId, role, artifactId, query);

    private static FileLibraryExcerptCommand File(
        WorldRole role = WorldRole.GM,
        Guid? documentId = null,
        IReadOnlyList<Guid>? chunkIds = null,
        int? pageFrom = null,
        int? pageTo = null,
        Guid? artifactId = null) =>
        new(WorldId, role == WorldRole.GM ? GmId : PlayerId, role, documentId ?? DocumentId, chunkIds ?? [], pageFrom, pageTo, artifactId);

    #region Search

    [Test]
    [Category("Authorization")]
    public async Task Search_AsPlayer_Returns403()
    {
        var result = await _sut.SearchAsync(Search(WorldRole.Player, ThistleholdId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_retriever.LastQuestion, Is.Null);
    }

    [Test]
    public async Task Search_ByArtifact_QueriesWithNameAndSummary_OverGmShelves_AttributedToTheGm()
    {
        _retriever.Passages.Add(new KnowledgePassage
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = DocumentId,
            DocumentTitle = "Player's Guide",
            Page = 42,
            Text = "Thistlehold sits on the river.",
            ReferenceId = "passage:x"
        });

        var result = await _sut.SearchAsync(Search(artifactId: ThistleholdId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_retriever.LastQuestion, Does.Contain("Thistlehold").And.Contain("river city"));
        Assert.That(_retriever.LastAllowedScopes, Is.EquivalentTo([VisibilityScope.PartyVisible, VisibilityScope.GMOnly]));
        Assert.That(_retriever.LastAttributedUserId, Is.EqualTo(GmId));
        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Page, Is.EqualTo(42));
        Assert.That(result.Value![0].DocumentTitle, Is.EqualTo("Player's Guide"));
    }

    [Test]
    public async Task Search_ExplicitQuery_WinsOverTheArtifact()
    {
        await _sut.SearchAsync(Search(artifactId: ThistleholdId, query: "  the Thistle Council  "), CancellationToken.None);

        Assert.That(_retriever.LastQuestion, Is.EqualTo("the Thistle Council"));
    }

    [Test]
    public async Task Search_NeitherQueryNorArtifact_Returns400()
    {
        var result = await _sut.SearchAsync(Search(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Search_ArtifactOfAnotherWorld_Returns404()
    {
        var foreign = Guid.NewGuid();
        _artifacts.Seed(new Artifact { Id = foreign, WorldId = OtherWorldId, Name = "Elsewhere", Type = ArtifactType.Location });

        var result = await _sut.SearchAsync(Search(artifactId: foreign), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    #endregion

    #region File

    [Test]
    [Category("Authorization")]
    public async Task File_AsObserver_Returns403_AndNothingIsCreated()
    {
        var chunk = SeedChunk(0, 42, "text");

        var result = await _sut.FileAsync(File(WorldRole.Observer, chunkIds: [chunk.ChunkId]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_sources.Sources, Is.Empty);
    }

    [Test]
    public async Task File_ByChunkIds_ComposesInReadingOrder_InheritsTheShelf_AndIsQueued()
    {
        var third = SeedChunk(2, 44, "Third passage.");
        var first = SeedChunk(0, 42, "First passage.");
        var second = SeedChunk(1, 43, "Second passage.");

        var result = await _sut.FileAsync(
            File(chunkIds: [third.ChunkId, first.ChunkId, second.ChunkId], artifactId: ThistleholdId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var source = result.Value!;
        Assert.That(source.Type, Is.EqualTo(SourceType.LibraryExcerpt));
        Assert.That(source.Title, Is.EqualTo("Thistlehold — Player's Guide, pp. 42–44"));
        Assert.That(source.Body, Does.Contain("First passage.\n\nSecond passage.\n\nThird passage."));
        Assert.That(source.Body, Does.StartWith("Excerpt from “Player's Guide”, pp. 42–44, filed for the codex entry “Thistlehold”."));
        Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.GMOnly), "the document's shelf is the excerpt's audience");
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
        Assert.That(source.LibraryDocumentId, Is.EqualTo(DocumentId));
        Assert.That((source.LibraryPageFrom, source.LibraryPageTo), Is.EqualTo((42, 44)));
        Assert.That(source.CreatedByUserId, Is.EqualTo(GmId));
        Assert.That(source.ExtractionEnabled, Is.True);
        Assert.That(source.CampaignId, Is.Null);
        Assert.That(source.OccurredAt, Is.Null);
        Assert.That(_queue.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_queue.SentMessages[0].SourceId, Is.EqualTo(source.Id));
    }

    [Test]
    public async Task File_PartyShelfDocument_YieldsAPartyVisibleExcerpt()
    {
        var partyDoc = Guid.NewGuid();
        _documents.Seed(Document(partyDoc, WorldId, VisibilityScope.PartyVisible, LibraryDocumentStatus.Indexed));
        var chunk = SeedChunk(0, 7, "The river trade.", partyDoc);

        var result = await _sut.FileAsync(File(documentId: partyDoc, chunkIds: [chunk.ChunkId]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(result.Value!.Title, Is.EqualTo("Player's Guide, p. 7"));
    }

    [Test]
    public async Task File_ChunkOfAnotherDocument_Returns400()
    {
        var elsewhere = SeedChunk(0, 1, "Not this book.", Guid.NewGuid());

        var result = await _sut.FileAsync(File(chunkIds: [elsewhere.ChunkId]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("chunk_not_found"));
        Assert.That(_sources.Sources, Is.Empty);
    }

    [Test]
    public async Task File_DocumentNotIndexed_Returns409()
    {
        var pending = Guid.NewGuid();
        _documents.Seed(Document(pending, WorldId, VisibilityScope.GMOnly, LibraryDocumentStatus.Indexing));

        var result = await _sut.FileAsync(File(documentId: pending, pageFrom: 1, pageTo: 2), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("document_not_indexed"));
    }

    [Test]
    public async Task File_DocumentOfAnotherWorld_Returns404()
    {
        var foreign = Guid.NewGuid();
        _documents.Seed(Document(foreign, OtherWorldId, VisibilityScope.PartyVisible, LibraryDocumentStatus.Indexed));

        var result = await _sut.FileAsync(File(documentId: foreign, pageFrom: 1, pageTo: 2), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task File_ByPageRange_TakesTheChunksStartingInRange()
    {
        SeedChunk(0, 41, "Before.");
        SeedChunk(1, 42, "Districts.");
        SeedChunk(2, 43, "Council.");
        SeedChunk(3, 46, "After.");

        var result = await _sut.FileAsync(File(pageFrom: 42, pageTo: 45), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.Body, Does.Contain("Districts.\n\nCouncil."));
        Assert.That(result.Value!.Body, Does.Not.Contain("Before.").And.Not.Contain("After."));
        Assert.That((result.Value!.LibraryPageFrom, result.Value!.LibraryPageTo), Is.EqualTo((42, 43)));
    }

    [Test]
    public async Task File_PageRangeWithNoPassages_Returns400()
    {
        SeedChunk(0, 1, "Only page one.");

        var result = await _sut.FileAsync(File(pageFrom: 40, pageTo: 45), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("no_passages_in_range"));
    }

    [TestCase(0, 3)]
    [TestCase(5, 2)]
    public async Task File_MalformedPageRange_Returns400(int from, int to)
    {
        var result = await _sut.FileAsync(File(pageFrom: from, pageTo: to), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task File_PageRangeOverTheBound_Returns400()
    {
        for (var i = 0; i < 4; i++)
        {
            SeedChunk(i, 42 + i, $"Passage {i}.");
        }

        var result = await _sut.FileAsync(File(pageFrom: 42, pageTo: 45), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("excerpt_too_large"));
        Assert.That(_sources.Sources, Is.Empty);
    }

    [Test]
    public async Task File_ChunkIdsOverTheBound_Returns400_WithoutReadingChunks()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();

        var result = await _sut.FileAsync(File(chunkIds: ids), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("excerpt_too_large"));
    }

    [Test]
    public async Task File_NeitherChunksNorRange_Returns400()
    {
        var result = await _sut.FileAsync(File(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task File_ArtifactOfAnotherWorld_Returns404()
    {
        var chunk = SeedChunk(0, 42, "text");
        var foreign = Guid.NewGuid();
        _artifacts.Seed(new Artifact { Id = foreign, WorldId = OtherWorldId, Name = "Elsewhere", Type = ArtifactType.Location });

        var result = await _sut.FileAsync(File(chunkIds: [chunk.ChunkId], artifactId: foreign), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_sources.Sources, Is.Empty);
    }

    [Test]
    public async Task File_QueueDown_Returns502_AndTheExcerptWaitsAtReady()
    {
        var chunk = SeedChunk(0, 42, "text");
        _queue.ConfigureToFail();

        var result = await _sut.FileAsync(File(chunkIds: [chunk.ChunkId]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(502));
        var stored = _sources.Sources.Single();
        Assert.That(stored.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Ready), "the same revert a captured note gets");
    }

    #endregion
}
