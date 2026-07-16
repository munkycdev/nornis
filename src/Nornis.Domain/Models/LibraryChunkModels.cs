namespace Nornis.Domain.Models;

/// <summary>A chunk paired with its embedding for persistence — the vector never lives on
/// the entity (it is an Infrastructure shadow property on the SQL side).</summary>
public sealed record LibraryChunkWrite(
    Entities.LibraryChunk Chunk,
    float[] Embedding);

/// <summary>A similarity-search hit, pre-joined with what a citation needs.</summary>
public sealed record LibraryChunkHit(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    int Page,
    string Text,
    double Distance);
