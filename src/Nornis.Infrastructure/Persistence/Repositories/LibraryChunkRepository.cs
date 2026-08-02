using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Persistence.Configurations;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class LibraryChunkRepository : ILibraryChunkRepository
{
    private readonly NornisDbContext _context;

    public LibraryChunkRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task ReplaceForDocumentAsync(
        Guid documentId,
        IReadOnlyList<LibraryChunkWrite> chunks,
        CancellationToken cancellationToken = default)
    {
        // Relational-only by nature, so the bulk call stays raw here: the embedding is a
        // SqlVector shadow property and this opens a real transaction. Nothing about this
        // method could run on the InMemory provider, and pretending otherwise with the
        // guarded helper would only hide that.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await _context.LibraryChunks
            .Where(c => c.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var write in chunks)
        {
            _context.LibraryChunks.Add(write.Chunk);
            // The vector is a shadow property — set through the change tracker, never the entity.
            _context.Entry(write.Chunk)
                .Property(LibraryChunkConfiguration.EmbeddingProperty)
                .CurrentValue = new SqlVector<float>(write.Embedding);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        // Unlike the replace above, this one is reachable from the API's InMemory tests —
        // deleting a document deletes its chunks.
        _context.DeleteWhereAsync<LibraryChunk>(c => c.DocumentId == documentId, cancellationToken);

    public async Task<IReadOnlyList<LibraryChunkHit>> ListByDocumentOrdsAsync(
        Guid documentId,
        IReadOnlyList<int> ords,
        CancellationToken cancellationToken = default)
    {
        return await _context.LibraryChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId && ords.Contains(c.Ord))
            .OrderBy(c => c.Ord)
            .Select(c => new LibraryChunkHit(c.Id, c.DocumentId, c.Document.Title, c.Ord, c.Page, c.Text, 0d))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryChunkHit>> SearchAsync(
        Guid worldId,
        float[] queryEmbedding,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var query = new SqlVector<float>(queryEmbedding);

        // Exact KNN scan over the world's chunks — at sourcebook scale (a few thousand
        // rows) this is milliseconds; visibility gates on the owning document.
        return await _context.LibraryChunks
            .AsNoTracking()
            .Where(c => c.WorldId == worldId)
            .Where(c => c.Document.Status == LibraryDocumentStatus.Indexed
                && allowedVisibilities.Contains(c.Document.Visibility))
            .OrderBy(c => EF.Functions.VectorDistance(
                "cosine",
                EF.Property<SqlVector<float>>(c, LibraryChunkConfiguration.EmbeddingProperty),
                query))
            .Take(topK)
            .Select(c => new LibraryChunkHit(
                c.Id,
                c.DocumentId,
                c.Document.Title,
                c.Ord,
                c.Page,
                c.Text,
                EF.Functions.VectorDistance(
                    "cosine",
                    EF.Property<SqlVector<float>>(c, LibraryChunkConfiguration.EmbeddingProperty),
                    query)))
            .ToListAsync(cancellationToken);
    }
}
