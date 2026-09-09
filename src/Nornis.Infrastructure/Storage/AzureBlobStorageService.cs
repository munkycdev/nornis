using System.Net;
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
///
/// Takes the client rather than a connection string so that how the process authenticates
/// is decided once, in <c>AzureClients</c>, and not again here. The one place that difference
/// shows is SAS: an account key can sign a SAS locally, an identity cannot, so under an
/// identity the SAS is signed with a user delegation key fetched from the service instead.
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorageService
{
    public const string DefaultContainerName = "nornis-library";

    /// <summary>
    /// How long a fetched user delegation key is asked to live. Every SAS this service signs
    /// expires within minutes, so one key covers many SAS; refreshed while it still has
    /// comfortably more life than the longest SAS it will sign.
    /// </summary>
    private static readonly TimeSpan DelegationKeyLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan DelegationKeyRefreshMargin = TimeSpan.FromMinutes(20);

    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly string _containerName;

    public AzureBlobStorageService(
        BlobServiceClient blobServiceClient,
        string containerName,
        ILogger<AzureBlobStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        _logger = logger;
        _containerName = containerName;
        _blobServiceClient = blobServiceClient;

        // Container creation used to happen right here, synchronously, in the constructor.
        // Two problems, both real: it blocked a DI resolution on a network round trip, and it
        // threw a raw RequestFailedException from a code path with no translation — so a
        // transient storage 503 at first use reached library indexing as an exception the
        // classifier could not type-match, and the document was permanently marked
        // IndexFailed over a blip. Deferred to first use and translated like everything else.
    }

    private readonly SemaphoreSlim _containerGate = new(1, 1);
    private bool _containerReady;

    private readonly SemaphoreSlim _delegationKeyGate = new(1, 1);
    private UserDelegationKey? _delegationKey;

    /// <summary>
    /// Ensures the container exists, once per process, on the first operation that needs it.
    ///
    /// Deliberately not a <c>Lazy&lt;Task&gt;</c>: that caches a faulted task forever, so a
    /// single 503 during the first call would leave the service permanently broken — the
    /// exact over-correction this fix exists to avoid. A failure here leaves the flag unset
    /// and the next call tries again.
    /// </summary>
    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerReady)
        {
            return;
        }

        await _containerGate.WaitAsync(cancellationToken);
        try
        {
            if (_containerReady)
            {
                return;
            }

            await _blobServiceClient
                .GetBlobContainerClient(_containerName)
                .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

            _containerReady = true;
        }
        catch (RequestFailedException ex)
        {
            throw new HttpRequestException(
                $"Blob container init failed for {_containerName}: HTTP {ex.Status}",
                ex,
                (HttpStatusCode)ex.Status);
        }
        finally
        {
            _containerGate.Release();
        }
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
        return GenerateSasUrlAsync(blobPath, sasBuilder, cancellationToken);
    }

    public Task<string> GenerateDownloadSasUrlAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var sasBuilder = CreateSasBuilder(blobPath);
        sasBuilder.SetPermissions(BlobSasPermissions.Read);
        return GenerateSasUrlAsync(blobPath, sasBuilder, cancellationToken);
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
        await EnsureContainerAsync(cancellationToken);
        try
        {
            return await GetBlobClient(blobPath).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException($"Blob not found: {blobPath}");
        }
        catch (RequestFailedException ex)
        {
            // Everything else is translated so the application layer can tell a retryable storage
            // failure from a permanent one without depending on the Azure SDK. Left untranslated,
            // a 503 ServerBusy reaching library indexing was classified by matching the exception
            // text — and a wrong answer there permanently fails a document the GM then has to
            // reindex by hand.
            throw new HttpRequestException(
                $"Blob read failed for {blobPath}: HTTP {ex.Status}",
                ex,
                (HttpStatusCode)ex.Status);
        }
    }

    public async Task UploadAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        var blobClient = GetBlobClient(blobPath);
        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task DeleteBlobAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        var blobClient = GetBlobClient(blobPath);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        // Traits and states are explicit since Azure.Storage.Blobs 12.29: the overload with
        // defaults is gone. None/None is what the defaults were — names only, no metadata, no
        // snapshots or soft-deleted blobs — and this only ever needs the name to delete by.
        await foreach (var blob in containerClient.GetBlobsAsync(
            BlobTraits.None, BlobStates.None, prefix, cancellationToken))
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

    /// <summary>
    /// A shared-key client signs the SAS itself. An identity client cannot — there is no key
    /// in the process — so it signs with a user delegation key the service issues to the
    /// identity, which is scoped to what that identity may do and expires on its own. The
    /// browser sees the same shape of URL either way.
    /// </summary>
    private async Task<string> GenerateSasUrlAsync(string blobPath, BlobSasBuilder sasBuilder, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(blobPath);

        if (blobClient.CanGenerateSasUri)
        {
            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        var key = await GetDelegationKeyAsync(sasBuilder.ExpiresOn, cancellationToken);
        var uri = new BlobUriBuilder(blobClient.Uri)
        {
            Sas = sasBuilder.ToSasQueryParameters(key, _blobServiceClient.AccountName)
        };
        return uri.ToUri().ToString();
    }

    private async Task<UserDelegationKey> GetDelegationKeyAsync(DateTimeOffset mustOutlive, CancellationToken cancellationToken)
    {
        var current = _delegationKey;
        if (current is not null && current.SignedExpiresOn > mustOutlive + DelegationKeyRefreshMargin)
        {
            return current;
        }

        await _delegationKeyGate.WaitAsync(cancellationToken);
        try
        {
            current = _delegationKey;
            if (current is not null && current.SignedExpiresOn > mustOutlive + DelegationKeyRefreshMargin)
            {
                return current;
            }

            var now = DateTimeOffset.UtcNow;
            var fresh = await _blobServiceClient.GetUserDelegationKeyAsync(
                now.AddMinutes(-5), now + DelegationKeyLifetime, cancellationToken);
            _delegationKey = fresh.Value;
            return fresh.Value;
        }
        finally
        {
            _delegationKeyGate.Release();
        }
    }

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
