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

    Task DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Nearest chunks to the question across the world's Indexed documents within
    /// the allowed visibility scopes, ordered by cosine distance (closest first).</summary>
    Task<IReadOnlyList<LibraryChunkHit>> SearchAsync(
        Guid worldId,
        float[] queryEmbedding,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        int topK,
        CancellationToken cancellationToken = default);
}
