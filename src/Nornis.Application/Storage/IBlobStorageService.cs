namespace Nornis.Application.Storage;

/// <summary>Size and content type reported by blob storage for an uploaded blob.</summary>
public sealed record BlobMetadata(long SizeBytes, string ContentType);

/// <summary>
/// Blob storage for library documents. Uploads use the SAS handshake: the API hands the
/// browser a short-lived write URL and the bytes never pass through the server; downloads
/// are short-lived read URLs minted on demand (never persisted — they expire).
/// </summary>
public interface IBlobStorageService
{
    /// <summary>Deterministic blob path: worlds/{worldId}/library/{documentId}/{sanitized-filename}.</summary>
    string BuildBlobPath(Guid worldId, Guid documentId, string fileName);

    /// <summary>Deterministic blob path for source attachments: worlds/{worldId}/sources/{sourceId}/{sanitized-filename}.</summary>
    string BuildSourceBlobPath(Guid worldId, Guid sourceId, string fileName);

    /// <summary>Write-only SAS URL (Create|Write), 15-minute expiry, for direct browser upload.</summary>
    Task<string> GenerateUploadSasUrlAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>Read-only SAS URL, 15-minute expiry.</summary>
    Task<string> GenerateDownloadSasUrlAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>Null when the blob does not exist — the confirm step's existence check.</summary>
    Task<BlobMetadata?> GetBlobMetadataAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>Server-side read stream (worker indexing). Throws FileNotFoundException when missing.</summary>
    Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken = default);

    Task DeleteBlobAsync(string blobPath, CancellationToken cancellationToken = default);
}
