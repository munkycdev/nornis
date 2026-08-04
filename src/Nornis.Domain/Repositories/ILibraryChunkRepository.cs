using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface ILibraryChunkRepository
{
    /// <summary>Atomically replaces a document's chunks (delete-then-insert) — reindexing
    /// must never leave a mix of old and new passages.</summary>
    Task ReplaceForDocumentAsync(
        Guid documentId,
        IReadOnlyList<LibraryChunkWrite> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds one batch of chunks to a document already being reindexed. The batched half of
    /// <see cref="ReplaceForDocumentAsync"/>, for the indexing pipeline: it deletes once with
    /// <see cref="DeleteForDocumentAsync"/> and then appends batch by batch, so peak memory is
    /// one batch of embeddings rather than every embedding in the document at once.
    /// <para>
    /// Safe to leave half-written because <see cref="SearchAsync"/> only reads chunks whose
    /// document is Indexed, and a document reaches that status after the last batch lands.
    /// </para>
    /// </summary>
    Task AppendForDocumentAsync(
        IReadOnlyList<LibraryChunkWrite> chunks,
        CancellationToken cancellationToken = default);

    Task DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Nearest chunks to the question across the world's Indexed documents within
    /// the allowed visibility scopes, ordered by cosine distance (closest first).</summary>
    Task<IReadOnlyList<LibraryChunkHit>> SearchAsync(
        Guid worldId,
        float[] queryEmbedding,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        int topK,
        CancellationToken cancellationToken = default);

    /// <summary>Specific chunks of one document by ordinal — neighbor expansion around
    /// similarity hits, so answers that span a chunk boundary (class tables, level
    /// progressions) arrive whole.</summary>
    Task<IReadOnlyList<LibraryChunkHit>> ListByDocumentOrdsAsync(
        Guid documentId,
        IReadOnlyList<int> ords,
        CancellationToken cancellationToken = default);
}
