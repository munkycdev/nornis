using Nornis.Application.Ai;
using Nornis.Application.Messaging;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryLibraryDocumentRepository : ILibraryDocumentRepository
{
    private readonly List<LibraryDocument> _documents = [];

    public IReadOnlyList<LibraryDocument> Documents => _documents.AsReadOnly();

    public void Seed(params LibraryDocument[] documents) => _documents.AddRange(documents);

    public Task<LibraryDocument> CreateAsync(LibraryDocument document, CancellationToken cancellationToken = default)
    {
        _documents.Add(document);
        return Task.FromResult(document);
    }

    public Task<LibraryDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.FirstOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<LibraryDocument>> ListByWorldAsync(
        Guid worldId, IReadOnlyList<VisibilityScope> allowedVisibilities, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LibraryDocument>>(_documents
            .Where(d => d.WorldId == worldId && allowedVisibilities.Contains(d.Visibility))
            .OrderByDescending(d => d.CreatedAt)
            .ToList());

    public Task<bool> AnyIndexedAsync(
        Guid worldId, IReadOnlyList<VisibilityScope> allowedVisibilities, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.Any(d => d.WorldId == worldId
            && d.Status == LibraryDocumentStatus.Indexed
            && allowedVisibilities.Contains(d.Visibility)));

    public Task<LibraryDocument> UpdateAsync(LibraryDocument document, CancellationToken cancellationToken = default)
    {
        var index = _documents.FindIndex(d => d.Id == document.Id);
        if (index >= 0)
        {
            _documents[index] = document;
        }
        return Task.FromResult(document);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _documents.RemoveAll(d => d.Id == id);
        return Task.CompletedTask;
    }
}

public class InMemoryLibraryChunkRepository : ILibraryChunkRepository
{
    public Dictionary<Guid, IReadOnlyList<LibraryChunkWrite>> WritesByDocument { get; } = [];

    public List<LibraryChunkHit> SearchHits { get; } = [];

    /// <summary>All chunks in the store — the source for ListByDocumentOrdsAsync.</summary>
    public List<LibraryChunkHit> AllChunks { get; } = [];

    public Task ReplaceForDocumentAsync(Guid documentId, IReadOnlyList<LibraryChunkWrite> chunks, CancellationToken cancellationToken = default)
    {
        WritesByDocument[documentId] = chunks;
        return Task.CompletedTask;
    }

    public Task DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        WritesByDocument.Remove(documentId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LibraryChunkHit>> SearchAsync(
        Guid worldId, float[] queryEmbedding, IReadOnlyList<VisibilityScope> allowedVisibilities, int topK,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LibraryChunkHit>>(SearchHits.Take(topK).ToList());

    public Task<IReadOnlyList<LibraryChunkHit>> ListByDocumentOrdsAsync(
        Guid documentId, IReadOnlyList<int> ords, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LibraryChunkHit>>(AllChunks
            .Where(c => c.DocumentId == documentId && ords.Contains(c.Ord))
            .OrderBy(c => c.Ord)
            .ToList());
}

public class FakeBlobStorageService : IBlobStorageService
{
    public Dictionary<string, (byte[] Content, string ContentType)> Blobs { get; } = [];

    public List<string> DeletedPaths { get; } = [];

    public bool FailDeletes { get; set; }

    public string BuildBlobPath(Guid worldId, Guid documentId, string fileName) =>
        $"worlds/{worldId}/library/{documentId}/{fileName}";

    public string BuildSourceBlobPath(Guid worldId, Guid sourceId, string fileName) =>
        $"worlds/{worldId}/sources/{sourceId}/{fileName}";

    public Task<string> GenerateUploadSasUrlAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromResult($"https://blob.test/{blobPath}?sas=upload");

    public Task<string> GenerateDownloadSasUrlAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromResult($"https://blob.test/{blobPath}?sas=download");

    public Task<BlobMetadata?> GetBlobMetadataAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Blobs.TryGetValue(blobPath, out var blob)
            ? new BlobMetadata(blob.Content.Length, blob.ContentType)
            : null);

    public Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Blobs.TryGetValue(blobPath, out var blob)
            ? Task.FromResult<Stream>(new MemoryStream(blob.Content))
            : throw new FileNotFoundException($"Blob not found: {blobPath}");

    public Task DeleteBlobAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        if (FailDeletes)
        {
            throw new IOException("Simulated blob delete failure");
        }
        DeletedPaths.Add(blobPath);
        Blobs.Remove(blobPath);
        return Task.CompletedTask;
    }
}

public class FakePdfTextExtractor : IPdfTextExtractor
{
    public List<PdfPageText> Pages { get; } = [];

    public Exception? ThrowOnExtract { get; set; }

    public Task<IReadOnlyList<PdfPageText>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct)
    {
        if (ThrowOnExtract is not null)
        {
            throw ThrowOnExtract;
        }
        return Task.FromResult<IReadOnlyList<PdfPageText>>(Pages.ToList());
    }
}

public class FakeEmbeddingClient : IEmbeddingClient
{
    public List<IReadOnlyList<string>> Batches { get; } = [];

    public Exception? ThrowOnEmbed { get; set; }

    public int TokensPerInput { get; set; } = 10;

    public Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        if (ThrowOnEmbed is not null)
        {
            throw ThrowOnEmbed;
        }
        Batches.Add(inputs);
        var embeddings = inputs.Select((_, i) => new float[] { i, 1f, 2f }).ToList();
        return Task.FromResult(new EmbeddingResult(embeddings, inputs.Count * TokensPerInput));
    }
}

public class FakeLibraryIndexingQueueClient : ILibraryIndexingQueueClient
{
    public List<(Guid DocumentId, Guid WorldId)> Sent { get; } = [];

    public Exception? ThrowOnSend { get; set; }

    public Task SendIndexingMessageAsync(Guid documentId, Guid worldId, CancellationToken ct = default)
    {
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }
        Sent.Add((documentId, worldId));
        return Task.CompletedTask;
    }
}
