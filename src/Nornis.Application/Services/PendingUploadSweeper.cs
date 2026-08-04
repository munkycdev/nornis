using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Storage;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>How many rows and blobs one sweep removed, per kind.</summary>
public readonly record struct PendingUploadSweepResult(int Documents, int Attachments)
{
    public int Total => Documents + Attachments;
}

/// <summary>
/// Removes upload rows whose blob never arrived.
///
/// <para>
/// Both upload paths are a two-step handshake: the server creates a row and issues a write SAS,
/// the browser PUTs the bytes, then the server confirms. Nothing ever completed the first step
/// when the browser did not do its half — a closed tab, a dead connection, a file picker
/// abandoned mid-thought — so the row sat in PendingUpload forever, invisible to every listing
/// and to the owner, and any partial blob sat beside it in storage being billed for.
/// </para>
/// <para>
/// The age threshold is what makes this safe: an upload in flight is indistinguishable from one
/// that was abandoned, and only time tells them apart. It has to comfortably exceed the SAS
/// lifetime and the slowest plausible upload of a file at the size cap.
/// </para>
/// </summary>
public class PendingUploadSweeper : IPendingUploadSweeper
{
    private readonly ILibraryDocumentRepository _documents;
    private readonly ISourceAttachmentRepository _attachments;
    private readonly IBlobStorageService _blobStorage;
    private readonly UploadSweepOptions _options;
    private readonly ILogger<PendingUploadSweeper> _logger;

    public PendingUploadSweeper(
        ILibraryDocumentRepository documents,
        ISourceAttachmentRepository attachments,
        IBlobStorageService blobStorage,
        IOptions<UploadSweepOptions> options,
        ILogger<PendingUploadSweeper> logger)
    {
        _documents = documents;
        _attachments = attachments;
        _blobStorage = blobStorage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PendingUploadSweepResult> SweepAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(_options.AbandonedAfterHours);

        var documents = await _documents.ListAbandonedPendingUploadsAsync(cutoff, _options.MaxPerSweep, ct);
        var sweptDocuments = 0;
        foreach (var document in documents)
        {
            if (await DeleteBlobAsync(document.BlobPath, "library document", document.Id, ct))
            {
                await _documents.DeleteAsync(document.Id, ct);
                sweptDocuments++;
            }
        }

        var attachments = await _attachments.ListAbandonedPendingUploadsAsync(cutoff, _options.MaxPerSweep, ct);
        var sweptAttachments = 0;
        foreach (var attachment in attachments)
        {
            if (await DeleteBlobAsync(attachment.BlobPath, "source attachment", attachment.Id, ct))
            {
                await _attachments.DeleteAsync(attachment.Id, ct);
                sweptAttachments++;
            }
        }

        var result = new PendingUploadSweepResult(sweptDocuments, sweptAttachments);
        if (result.Total > 0)
        {
            _logger.LogInformation(
                "Swept {Documents} abandoned library uploads and {Attachments} abandoned attachments older than {Hours}h",
                result.Documents, result.Attachments, _options.AbandonedAfterHours);
        }

        return result;
    }

    /// <summary>
    /// Blob first, row second, and the row stays if the blob will not go. The other order leaks
    /// the blob permanently — once the row is gone nothing remembers the path, and an orphan in
    /// storage is billed for with no way left to find it. A row that outlives one sweep is
    /// simply swept again next time.
    /// </summary>
    private async Task<bool> DeleteBlobAsync(string blobPath, string kind, Guid id, CancellationToken ct)
    {
        try
        {
            await _blobStorage.DeleteBlobAsync(blobPath, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not delete the blob for abandoned {Kind} {Id} at {BlobPath}; leaving the row for the next sweep",
                kind, id, blobPath);
            return false;
        }
    }
}
