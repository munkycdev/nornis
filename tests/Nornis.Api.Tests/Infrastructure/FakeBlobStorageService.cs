using Nornis.Application.Storage;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>In-memory blob store for Library integration tests. Seed <see cref="Blobs"/>
/// keyed by blob path to simulate a browser having PUT the file to the SAS URL.</summary>
public class FakeBlobStorageService : IBlobStorageService
{
    public Dictionary<string, (byte[] Content, string ContentType)> Blobs { get; } = [];

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
            : throw new FileNotFoundException(blobPath);

    public Task DeleteBlobAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        Blobs.Remove(blobPath);
        return Task.CompletedTask;
    }
}
