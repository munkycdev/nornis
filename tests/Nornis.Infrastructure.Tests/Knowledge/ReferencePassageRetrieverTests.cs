using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Nornis.Application.Configuration;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Knowledge;

namespace Nornis.Infrastructure.Tests.Knowledge;

[TestFixture]
public class ReferencePassageRetrieverTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid DocId = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private InMemoryLibraryChunkRepository _chunks = null!;
    private FakeEmbeddingClient _embeddings = null!;
    private ReferencePassageRetriever _sut = null!;
    private LibraryOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _chunks = new InMemoryLibraryChunkRepository();
        _embeddings = new FakeEmbeddingClient();
        _options = new LibraryOptions();
        _sut = new ReferencePassageRetriever(_documents, _chunks, _embeddings,
            new InMemoryAiUsageRecordRepository(), Options.Create(_options),
            NullLogger<ReferencePassageRetriever>.Instance);
    }

    private void SeedIndexedDocument() =>
        _documents.Seed(new LibraryDocument
        {
            Id = DocId,
            WorldId = WorldId,
            Title = "Players Guide",
            FileName = "pg.pdf",
            ContentType = "application/pdf",
            BlobPath = "x",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = VisibilityScope.GMOnly,
            Status = LibraryDocumentStatus.Indexed,
            UploadedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static LibraryChunkHit Chunk(int ord, string text, double distance = 0.5) =>
        new(Guid.NewGuid(), DocId, "Players Guide", ord, 100 + ord, text, distance);

    [Test]
    public async Task Retrieve_NoIndexedDocuments_SkipsEmbeddingEntirely()
    {
        var passages = await _sut.RetrieveAsync("question", WorldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(passages, Is.Empty);
        Assert.That(_embeddings.Batches, Is.Empty, "no embedding cost when nothing is indexed");
    }

    [Test]
    public async Task Retrieve_ExpandsHitsWithNeighbors_InReadingOrder()
    {
        SeedIndexedDocument();
        var before = Chunk(4, "At 9th level...");
        var hit = Chunk(5, "At 13th level...");
        var after = Chunk(6, "At 17th level...");
        _chunks.AllChunks.AddRange([before, hit, after, Chunk(20, "unrelated")]);
        _chunks.SearchHits.Add(hit);

        var passages = await _sut.RetrieveAsync("level 10?", WorldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(passages.Select(p => p.ChunkId),
            Is.EqualTo(new[] { before.ChunkId, hit.ChunkId, after.ChunkId }),
            "the hit's neighbors arrive with it, in document reading order");
    }

    [Test]
    public async Task Retrieve_CapRespected_SeedsSurvive()
    {
        SeedIndexedDocument();
        _options.MaxContextPassages = 4;
        for (var i = 0; i < 30; i += 3)
        {
            _chunks.AllChunks.Add(Chunk(i, $"chunk {i}"));
        }
        var seeds = _chunks.AllChunks.Take(3).ToList();
        _chunks.SearchHits.AddRange(seeds);
        _chunks.AllChunks.AddRange(seeds.Select(s => Chunk(s.Ord + 1, "neighbor")));

        var passages = await _sut.RetrieveAsync("q", WorldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(passages, Has.Count.LessThanOrEqualTo(4));
        foreach (var seed in seeds)
        {
            Assert.That(passages.Select(p => p.ChunkId), Does.Contain(seed.ChunkId),
                "similarity seeds must never be evicted by neighbors");
        }
    }

    [Test]
    public async Task Retrieve_NeighborRadiusZero_ReturnsSeedsOnly()
    {
        SeedIndexedDocument();
        _options.NeighborRadius = 0;
        var hit = Chunk(5, "hit");
        _chunks.AllChunks.AddRange([Chunk(4, "before"), hit, Chunk(6, "after")]);
        _chunks.SearchHits.Add(hit);

        var passages = await _sut.RetrieveAsync("q", WorldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(passages.Select(p => p.ChunkId), Is.EqualTo(new[] { hit.ChunkId }));
    }
}
