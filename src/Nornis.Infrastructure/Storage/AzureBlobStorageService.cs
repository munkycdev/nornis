using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Nornis.Application.Storage;

namespace Nornis.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage implementation, ported from Chronicis's BlobStorageService and
/// pointed at the shared stchronicis account with Nornis's own container. Registered as
/// a singleton: BlobServiceClient is thread-safe and the container-exists check runs once.
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorageService
{
    public const string DefaultContainerName = "nornis-library";

    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly string _containerName;

    public AzureBlobStorageService(
        string connectionString,
        string containerName,
        ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;
        _containerName = containerName;
        _blobServiceClient = new BlobServiceClient(connectionString);

        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        containerClient.CreateIfNotExists(PublicAccessType.None);
    }

    public string BuildBlobPath(Guid worldId, Guid documentId, string fileName)
    {
        var sanitized = SanitizeFileName(fileName);
        return $"worlds/{worldId}/library/{documentId}/{sanitized}";
    }

    public string BuildSourceBlobPath(Guid worldId, Guid sourceId, string fileName)
    {
        var sanitized = SanitizeFileName(fileName);
        return $"worlds/{worldId}/sources/{sourceId}/{sanitized}";
    }

    public Task<string> GenerateUploadSasUrlAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var sasBuilder = CreateSasBuilder(blobPath);
        sasBuilder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);
        return Task.FromResult(GenerateSasUrl(blobPath, sasBuilder));
    }

    public Task<string> GenerateDownloadSasUrlAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var sasBuilder = CreateSasBuilder(blobPath);
        sasBuilder.SetPermissions(BlobSasPermissions.Read);
        return Task.FromResult(GenerateSasUrl(blobPath, sasBuilder));
    }

    /// <remarks>
    /// No pre-flight <c>ExistsAsync</c>: it issues its own Get Blob Properties request, so the
    /// pair cost two billed transactions to answer one question — and a 404 is billed too.
    /// Catching the 404 from the call we already need is exactly equivalent and half the price.
    /// The not-found case is distinguished from a real fault so that a 403 or a throttle no
    /// longer masquerades as "the upload never arrived".
    /// </remarks>
    public async Task<BlobMetadata?> GetBlobMetadataAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var properties = await GetBlobClient(blobPath)
                .GetPropertiesAsync(cancellationToken: cancellationToken);
            return new BlobMetadata(properties.Value.ContentLength, properties.Value.ContentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Error getting blob metadata for {BlobPath}", blobPath);
            return null;
        }
    }

    /// <remarks>
    /// Same reasoning as <see cref="GetBlobMetadataAsync"/>. <c>OpenReadAsync</c> issues its
    /// first range request eagerly, so a missing blob still surfaces here as a 404 rather than
    /// deferring to the first <c>Read</c> — callers that catch <see cref="FileNotFoundException"/>
    /// around the open keep working.
    /// </remarks>
    public async Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetBlobClient(blobPath).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException($"Blob not found: {blobPath}");
        }
    }

    public async Task UploadAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(blobPath);
        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task DeleteBlobAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(blobPath);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            await containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
        }
    }

    private BlobClient GetBlobClient(string blobPath) =>
        _blobServiceClient.GetBlobContainerClient(_containerName).GetBlobClient(blobPath);

    private BlobSasBuilder CreateSasBuilder(string blobPath) => new()
    {
        BlobContainerName = _containerName,
        BlobName = blobPath,
        Resource = "b",
        StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5), // clock-skew allowance
        ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15),
    };

    private string GenerateSasUrl(string blobPath, BlobSasBuilder sasBuilder) =>
        GetBlobClient(blobPath).GenerateSasUri(sasBuilder).ToString();

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        if (sanitized.Length > 200)
        {
            var extension = Path.GetExtension(sanitized);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = nameWithoutExt[..(200 - extension.Length)] + extension;
        }

        return sanitized;
    }
}
