using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Library documents: immutable reference files (sourcebooks, maps, handouts) in blob
/// storage. Upload is Chronicis's 3-step SAS handshake — request-upload creates a pending
/// row and hands the browser a write URL; confirm verifies the blob landed and queues PDF
/// indexing. Never touches the extraction pipeline.
/// </summary>
public class LibraryService : ILibraryService
{
    public const string PdfContentType = "application/pdf";

    /// <summary>An Indexing row older than this is presumed abandoned (worker died) and
    /// becomes deletable again.</summary>
    public const int StaleIndexingMinutes = 30;

    private static readonly FrozenDictionary<string, string> AllowedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = PdfContentType,
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly ILibraryDocumentRepository _documentRepository;
    private readonly ILibraryChunkRepository _chunkRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILibraryIndexingQueueClient _indexingQueueClient;
    private readonly LibraryOptions _options;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(
        ILibraryDocumentRepository documentRepository,
        ILibraryChunkRepository chunkRepository,
        IBlobStorageService blobStorage,
        ILibraryIndexingQueueClient indexingQueueClient,
        IOptions<LibraryOptions> options,
        ILogger<LibraryService> logger)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _blobStorage = blobStorage;
        _indexingQueueClient = indexingQueueClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>GM sees everything; everyone else sees the party shelf. Private is not a
    /// library concept — reference material is shared by nature.</summary>
    public static IReadOnlyList<VisibilityScope> GetAllowedScopes(WorldRole role) =>
        role == WorldRole.GM
            ? [VisibilityScope.PartyVisible, VisibilityScope.GMOnly]
            : [VisibilityScope.PartyVisible];

    public async Task<AppResult<LibraryUploadTicket>> RequestUploadAsync(RequestLibraryUploadCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole == WorldRole.Observer)
        {
            return AppResult<LibraryUploadTicket>.Fail(new AppError(403, "insufficient_role", "Observers cannot upload library documents."));
        }

        var title = command.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            return AppResult<LibraryUploadTicket>.Fail(new AppError(400, "validation_error", "Title is required and must be at most 200 characters."));
        }

        var extension = Path.GetExtension(command.FileName ?? string.Empty);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.TryGetValue(extension, out var expectedContentType))
        {
            return AppResult<LibraryUploadTicket>.Fail(new AppError(400, "unsupported_file_type",
                $"Unsupported file type '{extension}'. Allowed: {string.Join(", ", AllowedExtensions.Keys)}."));
        }

        if (command.SizeBytes <= 0 || command.SizeBytes > _options.MaxUploadSizeBytes)
        {
            return AppResult<LibraryUploadTicket>.Fail(new AppError(400, "validation_error",
                $"File size must be between 1 byte and {_options.MaxUploadSizeBytes / (1024 * 1024)} MB."));
        }

        if (!string.Equals(command.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            // Mirror Chronicis: warn but do not reject — browsers report odd MIME types.
            _logger.LogWarning("Library upload MIME mismatch: {ContentType} for {Extension}", command.ContentType, extension);
        }

        // A GMOnly document a player couldn't see makes no sense — clamp, don't reject.
        var visibility = command.ActingUserRole == WorldRole.GM ? command.Visibility : VisibilityScope.PartyVisible;
        if (visibility != VisibilityScope.PartyVisible && visibility != VisibilityScope.GMOnly)
        {
            visibility = VisibilityScope.GMOnly;
        }

        var now = DateTimeOffset.UtcNow;
        var document = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            Title = title,
            FileName = command.FileName!,
            ContentType = expectedContentType,
            SizeBytes = command.SizeBytes,
            Kind = command.Kind,
            Visibility = visibility,
            Status = LibraryDocumentStatus.PendingUpload,
            UploadedByUserId = command.ActingUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        document.BlobPath = _blobStorage.BuildBlobPath(command.WorldId, document.Id, command.FileName!);

        document = await _documentRepository.CreateAsync(document, ct);
        var uploadUrl = await _blobStorage.GenerateUploadSasUrlAsync(document.BlobPath, ct);

        return AppResult<LibraryUploadTicket>.Success(new LibraryUploadTicket(document, uploadUrl));
    }

    public async Task<AppResult<LibraryDocument>> ConfirmUploadAsync(Guid documentId, Guid worldId, Guid actingUserId, CancellationToken ct)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, ct);
        if (document is null || document.WorldId != worldId)
        {
            return AppResult<LibraryDocument>.Fail(new AppError(404, "not_found", "Library document not found."));
        }

        if (document.Status != LibraryDocumentStatus.PendingUpload)
        {
            return AppResult<LibraryDocument>.Fail(new AppError(409, "invalid_status", "Document has already been confirmed."));
        }

        var metadata = await _blobStorage.GetBlobMetadataAsync(document.BlobPath, ct);
        if (metadata is null)
        {
            return AppResult<LibraryDocument>.Fail(new AppError(400, "upload_not_found",
                "The file has not arrived in storage — upload it to the provided URL, then confirm."));
        }

        document.SizeBytes = metadata.SizeBytes;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        var isPdf = string.Equals(document.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase);
        // Commit the target status before enqueueing (same ordering as MarkSourceReady):
        // a message for a row still in PendingUpload would race the worker.
        document.Status = isPdf ? LibraryDocumentStatus.Indexing : LibraryDocumentStatus.Stored;
        document = await _documentRepository.UpdateAsync(document, ct);

        if (isPdf)
        {
            try
            {
                await _indexingQueueClient.SendIndexingMessageAsync(document.Id, worldId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue library indexing for {DocumentId}", document.Id);
                document.Status = LibraryDocumentStatus.Stored;
                document = await _documentRepository.UpdateAsync(document, ct);
                return AppResult<LibraryDocument>.Fail(new AppError(502, "enqueue_failed",
                    "The file is stored, but indexing could not be queued. Use reindex to retry."));
            }
        }

        return AppResult<LibraryDocument>.Success(document);
    }

    public async Task<AppResult<IReadOnlyList<LibraryDocument>>> ListAsync(Guid worldId, WorldRole role, CancellationToken ct)
    {
        var documents = await _documentRepository.ListByWorldAsync(worldId, GetAllowedScopes(role), ct);
        // Pending rows are upload dialogs that never finished — invisible until confirmed.
        var visible = documents.Where(d => d.Status != LibraryDocumentStatus.PendingUpload).ToList();
        return AppResult<IReadOnlyList<LibraryDocument>>.Success(visible);
    }

    public async Task<AppResult<LibraryDocument>> GetByIdAsync(Guid documentId, Guid worldId, WorldRole role, CancellationToken ct)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, ct);
        if (document is null || document.WorldId != worldId || !GetAllowedScopes(role).Contains(document.Visibility))
        {
            return AppResult<LibraryDocument>.Fail(new AppError(404, "not_found", "Library document not found."));
        }
        return AppResult<LibraryDocument>.Success(document);
    }

    public async Task<AppResult<LibraryDownload>> GetDownloadAsync(Guid documentId, Guid worldId, WorldRole role, CancellationToken ct)
    {
        var result = await GetByIdAsync(documentId, worldId, role, ct);
        if (!result.IsSuccess)
        {
            return AppResult<LibraryDownload>.Fail(result.Error!);
        }

        var document = result.Value!;
        var url = await _blobStorage.GenerateDownloadSasUrlAsync(document.BlobPath, ct);
        return AppResult<LibraryDownload>.Success(
            new LibraryDownload(url, document.FileName, document.ContentType, document.SizeBytes));
    }

    public async Task<AppResult<bool>> DeleteAsync(Guid documentId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var result = await GetByIdAsync(documentId, worldId, role, ct);
        if (!result.IsSuccess)
        {
            return AppResult<bool>.Fail(result.Error!);
        }

        var document = result.Value!;
        if (role != WorldRole.GM && document.UploadedByUserId != actingUserId)
        {
            return AppResult<bool>.Fail(new AppError(403, "insufficient_role", "Only the uploader or a GM can delete a library document."));
        }

        // Deleting mid-index races the worker's chunk-insert transaction (lock timeouts,
        // then a worker crash into a vanished row). Refuse until indexing settles — unless
        // the row has sat in Indexing so long the worker clearly died, which would otherwise
        // make the document undeletable.
        if (document.Status == LibraryDocumentStatus.Indexing
            && document.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-StaleIndexingMinutes))
        {
            return AppResult<bool>.Fail(new AppError(409, "indexing_in_progress",
                "This document is still being indexed — it can be deleted once indexing finishes."));
        }

        // Blob first, failure swallowed (per Chronicis): an orphaned blob beats an orphaned
        // row pointing at nothing. Chunks go with the row via FK cascade.
        try
        {
            await _blobStorage.DeleteBlobAsync(document.BlobPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blob delete failed for {DocumentId}; removing the row anyway", document.Id);
        }

        await _documentRepository.DeleteAsync(document.Id, ct);
        return AppResult<bool>.Success(true);
    }

    public async Task<AppResult<LibraryDocument>> ReindexAsync(Guid documentId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var result = await GetByIdAsync(documentId, worldId, role, ct);
        if (!result.IsSuccess)
        {
            return AppResult<LibraryDocument>.Fail(result.Error!);
        }

        var document = result.Value!;
        if (role != WorldRole.GM && document.UploadedByUserId != actingUserId)
        {
            return AppResult<LibraryDocument>.Fail(new AppError(403, "insufficient_role", "Only the uploader or a GM can reindex a library document."));
        }

        if (!string.Equals(document.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase))
        {
            return AppResult<LibraryDocument>.Fail(new AppError(400, "not_indexable", "Only PDF documents can be indexed."));
        }

        if (document.Status is not (LibraryDocumentStatus.Stored or LibraryDocumentStatus.Indexed or LibraryDocumentStatus.IndexFailed))
        {
            return AppResult<LibraryDocument>.Fail(new AppError(409, "invalid_status", "Document is not in a reindexable state."));
        }

        document.Status = LibraryDocumentStatus.Indexing;
        document.ErrorMessage = null;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        document = await _documentRepository.UpdateAsync(document, ct);

        try
        {
            await _indexingQueueClient.SendIndexingMessageAsync(document.Id, worldId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue library reindexing for {DocumentId}", document.Id);
            document.Status = LibraryDocumentStatus.IndexFailed;
            document.ErrorMessage = "Indexing could not be queued.";
            document = await _documentRepository.UpdateAsync(document, ct);
            return AppResult<LibraryDocument>.Fail(new AppError(502, "enqueue_failed", "Indexing could not be queued."));
        }

        return AppResult<LibraryDocument>.Success(document);
    }
}
