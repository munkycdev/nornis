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

    public async Task<BlobMetadata?> GetBlobMetadataAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = GetBlobClient(blobPath);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return new BlobMetadata(properties.Value.ContentLength, properties.Value.ContentType);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Error getting blob metadata for {BlobPath}", blobPath);
            return null;
        }
    }

    public async Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(blobPath);

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException($"Blob not found: {blobPath}");
        }

        return await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
    }

    public async Task DeleteBlobAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(blobPath);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
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
