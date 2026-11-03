using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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

    public const int MaxTitleLength = 200;

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
    private readonly IAdventureRepository _adventures;
    private readonly AdventurePrintDetails _printDetails;
    private readonly LibraryOptions _options;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(
        ILibraryDocumentRepository documentRepository,
        ILibraryChunkRepository chunkRepository,
        IBlobStorageService blobStorage,
        ILibraryIndexingQueueClient indexingQueueClient,
        IAdventureRepository adventures,
        AdventurePrintDetails printDetails,
        IOptions<LibraryOptions> options,
        ILogger<LibraryService> logger)
    {
        _printDetails = printDetails;
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _blobStorage = blobStorage;
        _indexingQueueClient = indexingQueueClient;
        _adventures = adventures;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>A print's Library title: the Adventure's, dated, cut to fit the title bound.</summary>
    public static string PrintTitle(string adventureTitle, DateTimeOffset at)
    {
        var suffix = $", printed {at:yyyy-MM-dd}";
        var room = Math.Max(MaxPrintTitleChars - suffix.Length, 1);
        var title = adventureTitle.Trim();
        return (title.Length > room ? title[..room].TrimEnd() : title) + suffix;
    }

    private const int MaxPrintTitleChars = 200;

    /// <summary>
    /// Points the Adventure at its newest print. The print itself is already stored and confirmed,
    /// so a failure here costs only the "last printed" line and is logged, not returned.
    /// </summary>
    private async Task RecordLastPrintAsync(Guid adventureId, Guid documentId, CancellationToken ct)
    {
        try
        {
            var adventure = await _adventures.GetWithSectionsAsync(adventureId, ct);
            if (adventure is null)
            {
                return;
            }

            adventure.LastPrintedDocumentId = documentId;
            await _adventures.UpdateAsync(adventure, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not record the last print {DocumentId} on adventure {AdventureId}", documentId, adventureId);
        }
    }

    /// <summary>GM sees everything; everyone else sees the party shelf. Private is not a
    /// library concept — reference material is shared by nature.</summary>
    /// <summary>The refusal for an upload-only action on a book from Bibliotaph (feature 31).</summary>
    private static AppError InBibliotaph(string why) =>
        new(409, "in_bibliotaph", $"This book {why}.");

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

        // A print filed from Prep (feature 29): the GM's alone, and its shape is fixed here, not
        // by the client, because the booklet was drafted with the world's Hidden truth.
        Adventure? printedFrom = null;
        PrintDetails? printDetails = null;
        if (command.AdventureId is { } adventureId)
        {
            if (command.ActingUserRole != WorldRole.GM)
            {
                return AppResult<LibraryUploadTicket>.Fail(new AppError(403, "insufficient_role", "Only the GM can file a Prep print."));
            }

            printedFrom = await _adventures.GetWithSectionsAsync(adventureId, ct);
            if (printedFrom is null || printedFrom.WorldId != command.WorldId)
            {
                return AppResult<LibraryUploadTicket>.Fail(new AppError(404, "not_found", "Adventure not found."));
            }

            // The print's details as they stand now (feature 32), kept with the document and
            // never changed: renaming the Adventure or editing its details later leaves them.
            printDetails = await _printDetails.ResolveAsync(printedFrom, ct);
            command = command with
            {
                Title = PrintTitle(printedFrom.Title, DateTimeOffset.UtcNow),
                Kind = LibraryDocumentKind.Handout,
                Visibility = VisibilityScope.GMOnly,
            };
        }

        var titleResult = NormalizeTitle(command.Title);
        if (!titleResult.IsSuccess)
        {
            return AppResult<LibraryUploadTicket>.Fail(titleResult.Error!);
        }
        var title = titleResult.Value!;

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
            AdventureId = printedFrom?.Id,
            PrintDetails = printDetails,
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

        // The cap in RequestUploadAsync validates a number the client sent; this validates the
        // file. Between the two sits a Create|Write SAS, so what actually lands in storage is
        // whatever the uploader chose to put there. Downstream, the indexing pipeline buffers a
        // whole document in RAM — so an unchecked blob here is a member with an OOM switch for
        // the worker, on a loop, and the row would carry the honest size while nothing had ever
        // compared it to the cap.
        if (metadata.SizeBytes > _options.MaxUploadSizeBytes)
        {
            await DeleteOversizedBlobAsync(document.BlobPath, metadata.SizeBytes, ct);

            // Left PendingUpload, exactly like the not-arrived case above: from here on this
            // document is one that never uploaded, and re-uploading through the same ticket is
            // the remedy. Confirming again just deletes again.
            return AppResult<LibraryDocument>.Fail(new AppError(400, "upload_too_large",
                $"The uploaded file is {metadata.SizeBytes / (1024 * 1024)} MB, over the "
                + $"{_options.MaxUploadSizeBytes / (1024 * 1024)} MB limit. It has been discarded."));
        }

        document.SizeBytes = metadata.SizeBytes;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        var isPdf = string.Equals(document.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase);
        // Commit the target status before enqueueing (same ordering as MarkSourceReady):
        // a message for a row still in PendingUpload would race the worker.
        document.Status = isPdf ? LibraryDocumentStatus.Indexing : LibraryDocumentStatus.Stored;
        document = await _documentRepository.UpdateAsync(document, ct);

        if (document.AdventureId is { } printedFromId)
        {
            await RecordLastPrintAsync(printedFromId, document.Id, ct);
        }

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
        // Pending rows are upload dialogs that never finished — invisible until confirmed. A
        // removed row is a book that left the world's Bibliotaph collection (feature 31).
        var visible = documents
            .Where(d => d.Status != LibraryDocumentStatus.PendingUpload && d.RemovedAt is null)
            .ToList();
        return AppResult<IReadOnlyList<LibraryDocument>>.Success(visible);
    }

    public async Task<AppResult<LibraryDocument>> GetByIdAsync(Guid documentId, Guid worldId, WorldRole role, CancellationToken ct)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, ct);
        if (document is null || document.WorldId != worldId || !GetAllowedScopes(role).Contains(document.Visibility)
            || document.RemovedAt is not null)
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
        if (document.Origin == LibraryDocumentOrigin.Bibliotaph)
        {
            return AppResult<LibraryDownload>.Fail(InBibliotaph("is read in Bibliotaph, not downloaded"));
        }

        var url = await _blobStorage.GenerateDownloadSasUrlAsync(document.BlobPath, ct);
        return AppResult<LibraryDownload>.Success(
            new LibraryDownload(url, document.FileName, document.ContentType, document.SizeBytes));
    }

    /// <summary>
    /// GM-only: moves a document between the party shelf and the GM's. Chunks carry no scope
    /// of their own — retrieval filters on the parent document — so the Loremaster starts (or
    /// stops) quoting the book on the next question, with no reindex.
    /// </summary>
    public async Task<AppResult<LibraryDocument>> SetVisibilityAsync(
        Guid documentId, Guid worldId, WorldRole role, VisibilityScope visibility, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<LibraryDocument>.Fail(new AppError(403, "insufficient_role",
                "Only GMs can change a library document's visibility."));
        }

        if (visibility is not (VisibilityScope.PartyVisible or VisibilityScope.GMOnly))
        {
            return AppResult<LibraryDocument>.Fail(new AppError(400, "invalid_visibility",
                "Library documents are either PartyVisible or GMOnly."));
        }

        var result = await GetByIdAsync(documentId, worldId, role, ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        var document = result.Value!;
        // No-op early: bumping UpdatedAt for nothing would also reset the stale-Indexing
        // clock that keeps an abandoned document deletable.
        if (document.Visibility == visibility)
        {
            return AppResult<LibraryDocument>.Success(document);
        }

        document.Visibility = visibility;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        document = await _documentRepository.UpdateAsync(document, ct);

        return AppResult<LibraryDocument>.Success(document);
    }

    /// <summary>
    /// GM-only: retitles a document. Nothing else moves — the file, its name in storage, and
    /// the indexed passages are untouched. This is the label on the shelf, not the book.
    /// </summary>
    public async Task<AppResult<LibraryDocument>> RenameAsync(
        Guid documentId, Guid worldId, WorldRole role, string? title, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<LibraryDocument>.Fail(new AppError(403, "insufficient_role",
                "Only GMs can rename a library document."));
        }

        var titleResult = NormalizeTitle(title);
        if (!titleResult.IsSuccess)
        {
            return AppResult<LibraryDocument>.Fail(titleResult.Error!);
        }

        var result = await GetByIdAsync(documentId, worldId, role, ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        var document = result.Value!;
        // No-op early, for the same reason SetVisibilityAsync does: bumping UpdatedAt for
        // nothing would reset the stale-Indexing clock that keeps an abandoned row deletable.
        if (document.Title == titleResult.Value)
        {
            return AppResult<LibraryDocument>.Success(document);
        }

        document.Title = titleResult.Value!;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        document = await _documentRepository.UpdateAsync(document, ct);

        return AppResult<LibraryDocument>.Success(document);
    }

    /// <summary>
    /// GM-only. Deleting a library document destroys the file and its indexed passages for
    /// everyone in the world, and a shelf a player can empty is not a shelf the GM can rely
    /// on mid-session — so uploading no longer carries the right to remove.
    /// </summary>
    public async Task<AppResult> DeleteAsync(Guid documentId, Guid worldId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult.Fail(new AppError(403, "insufficient_role", "Only GMs can delete a library document."));
        }

        var result = await GetByIdAsync(documentId, worldId, role, ct);
        if (!result.IsSuccess)
        {
            return AppResult.Fail(result.Error!);
        }

        var document = result.Value!;
        if (document.Origin == LibraryDocumentOrigin.Bibliotaph)
        {
            return AppResult.Fail(InBibliotaph("leaves the Library when it leaves the world's collection in Bibliotaph"));
        }

        // Deleting mid-index races the worker's chunk-insert transaction (lock timeouts,
        // then a worker crash into a vanished row). Refuse until indexing settles — unless
        // the row has sat in Indexing so long the worker clearly died, which would otherwise
        // make the document undeletable.
        if (document.Status == LibraryDocumentStatus.Indexing
            && document.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-StaleIndexingMinutes))
        {
            return AppResult.Fail(new AppError(409, "indexing_in_progress",
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
        return AppResult.Success();
    }

    public async Task<AppResult<LibraryDocument>> ReindexAsync(Guid documentId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var result = await GetByIdAsync(documentId, worldId, role, ct);
        if (!result.IsSuccess)
        {
            return AppResult<LibraryDocument>.Fail(result.Error!);
        }

        var document = result.Value!;
        if (document.Origin == LibraryDocumentOrigin.Bibliotaph)
        {
            return AppResult<LibraryDocument>.Fail(InBibliotaph("is searched by Bibliotaph, not indexed by Nornis"));
        }

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

    /// <summary>
    /// The one place a library title is judged. Upload and rename both come through here, so a
    /// title good enough to shelve a book under cannot be one the GM is then unable to set.
    /// </summary>
    private static AppResult<string> NormalizeTitle(string? title)
    {
        var normalized = title?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxTitleLength
            ? AppResult<string>.Fail(new AppError(400, "validation_error",
                $"Title is required and must be at most {MaxTitleLength} characters."))
            : AppResult<string>.Success(normalized);
    }

    /// <summary>
    /// Best-effort, and the failure is swallowed for the usual reason (an orphaned blob beats
    /// a lie to the caller) — but the log line is not optional. An oversized upload is a
    /// member doing something the client cannot do by accident, so a delete that fails is
    /// worth seeing.
    /// </summary>
    private async Task DeleteOversizedBlobAsync(string blobPath, long sizeBytes, CancellationToken ct)
    {
        _logger.LogWarning(
            "Discarding oversized upload at {BlobPath}: {SizeBytes} bytes against a {MaxBytes} byte cap",
            blobPath, sizeBytes, _options.MaxUploadSizeBytes);

        try
        {
            await _blobStorage.DeleteBlobAsync(blobPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete oversized upload at {BlobPath}", blobPath);
        }
    }
}
