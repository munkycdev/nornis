using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Blob-backed files hanging off a Source: page images of handwritten notes and the
/// ink-canvas stroke document. Same two-phase SAS handshake as the Library
/// (request-upload creates a PendingUpload row + write SAS; confirm verifies the blob
/// landed). Attachments are mutable only while the source is Draft/Ready/Failed — once
/// it queues, the pages are what the worker transcribed.
/// </summary>
public class SourceAttachmentService : ISourceAttachmentService
{
    public const long MaxAttachmentSizeBytes = 20 * 1024 * 1024; // 20 MB per page image / ink doc

    /// <summary>The single ink document per source always lives at a stable name so
    /// autosave overwrites in place.</summary>
    public const string InkDocumentFileName = "ink.json";

    private static readonly FrozenDictionary<string, string> AllowedImageExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Document attachments additionally accept PDF and plain text/markdown.</summary>
    private static readonly FrozenDictionary<string, string> AllowedDocumentExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".markdown"] = "text/markdown",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static FrozenDictionary<string, string> AllowedExtensionsFor(SourceAttachmentKind kind) =>
        kind == SourceAttachmentKind.Document ? AllowedDocumentExtensions : AllowedImageExtensions;

    /// <summary>Kinds whose content feeds derived text or map extraction — changing them
    /// invalidates whatever the worker previously derived.</summary>
    private static readonly SourceAttachmentKind[] DerivationKinds =
        [SourceAttachmentKind.ImageFile, SourceAttachmentKind.Document, SourceAttachmentKind.MapImage];

    private static readonly SourceProcessingStatus[] MutableStatuses =
        [SourceProcessingStatus.Draft, SourceProcessingStatus.Ready, SourceProcessingStatus.Failed];

    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceAttachmentRepository _attachmentRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<SourceAttachmentService> _logger;

    public SourceAttachmentService(
        ISourceRepository sourceRepository,
        ISourceAttachmentRepository attachmentRepository,
        IBlobStorageService blobStorage,
        ILogger<SourceAttachmentService> logger)
    {
        _sourceRepository = sourceRepository;
        _attachmentRepository = attachmentRepository;
        _blobStorage = blobStorage;
        _logger = logger;
    }

    public async Task<AppResult<SourceAttachmentUploadTicket>> RequestUploadAsync(
        RequestSourceAttachmentUploadCommand command, CancellationToken ct)
    {
        var gate = await LoadMutableSourceAsync(command.SourceId, command.WorldId, command.ActingUserId, command.ActingUserRole, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<SourceAttachmentUploadTicket>.Fail(gate.Error!);
        }

        string fileName;
        string contentType;

        if (command.Kind == SourceAttachmentKind.InkDocument)
        {
            fileName = InkDocumentFileName;
            contentType = "application/json";
        }
        else
        {
            var allowed = AllowedExtensionsFor(command.Kind);
            var extension = Path.GetExtension(command.FileName ?? string.Empty);
            if (string.IsNullOrEmpty(extension) || !allowed.TryGetValue(extension, out var expected))
            {
                return AppResult<SourceAttachmentUploadTicket>.Fail(new AppError(400, "unsupported_file_type",
                    $"Unsupported file type '{extension}'. Allowed: {string.Join(", ", allowed.Keys)}."));
            }

            fileName = command.FileName!;
            contentType = expected;

            if (!string.Equals(command.ContentType, expected, StringComparison.OrdinalIgnoreCase))
            {
                // Same policy as the Library: warn, don't reject — browsers report odd MIME types.
                _logger.LogWarning("Source attachment MIME mismatch: {ContentType} for {Extension}", command.ContentType, extension);
            }
        }

        if (command.Kind == SourceAttachmentKind.MapImage)
        {
            // One map per source, and only on Map sources — the extractor and the viewer
            // both assume a single map image.
            if (gate.Value!.Type != SourceType.Map)
            {
                return AppResult<SourceAttachmentUploadTicket>.Fail(new AppError(400, "invalid_kind",
                    "Map images can only be attached to Map sources."));
            }

            var hasMap = (await _attachmentRepository.ListBySourceAsync(command.SourceId, ct))
                .Any(a => a.Kind == SourceAttachmentKind.MapImage);
            if (hasMap)
            {
                return AppResult<SourceAttachmentUploadTicket>.Fail(new AppError(409, "duplicate_map_image",
                    "This source already has a map image. Delete it before uploading another."));
            }
        }

        if (command.SizeBytes is <= 0 or > MaxAttachmentSizeBytes)
        {
            return AppResult<SourceAttachmentUploadTicket>.Fail(new AppError(400, "validation_error",
                $"File size must be between 1 byte and {MaxAttachmentSizeBytes / (1024 * 1024)} MB."));
        }

        var now = DateTimeOffset.UtcNow;

        // One ink document per source, at a stable blob path: autosave requests a fresh
        // ticket for the SAME row and overwrites the blob in place.
        if (command.Kind == SourceAttachmentKind.InkDocument)
        {
            var existing = (await _attachmentRepository.ListBySourceAsync(command.SourceId, ct))
                .FirstOrDefault(a => a.Kind == SourceAttachmentKind.InkDocument);
            if (existing is not null)
            {
                var url = await _blobStorage.GenerateUploadSasUrlAsync(existing.BlobPath, ct);
                return AppResult<SourceAttachmentUploadTicket>.Success(new SourceAttachmentUploadTicket(existing, url));
            }
        }

        var attachment = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = command.SourceId,
            WorldId = command.WorldId,
            Kind = command.Kind,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = command.SizeBytes,
            Ord = command.Kind == SourceAttachmentKind.InkDocument ? 0 : command.Ord,
            Status = SourceAttachmentStatus.PendingUpload,
            CreatedAt = now,
            UpdatedAt = now,
        };
        // Ord in the file name keeps multi-page uploads with identical names distinct.
        var blobFileName = command.Kind == SourceAttachmentKind.InkDocument
            ? InkDocumentFileName
            : $"{attachment.Ord:D3}-{fileName}";
        attachment.BlobPath = _blobStorage.BuildSourceBlobPath(command.WorldId, command.SourceId, blobFileName);

        attachment = await _attachmentRepository.CreateAsync(attachment, ct);
        var uploadUrl = await _blobStorage.GenerateUploadSasUrlAsync(attachment.BlobPath, ct);

        return AppResult<SourceAttachmentUploadTicket>.Success(new SourceAttachmentUploadTicket(attachment, uploadUrl));
    }

    public async Task<AppResult<SourceAttachment>> ConfirmUploadAsync(
        Guid attachmentId, Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var gate = await LoadMutableSourceAsync(sourceId, worldId, actingUserId, role, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<SourceAttachment>.Fail(gate.Error!);
        }

        var attachment = await _attachmentRepository.GetByIdAsync(attachmentId, ct);
        if (attachment is null || attachment.SourceId != sourceId)
        {
            return AppResult<SourceAttachment>.Fail(new AppError(404, "not_found", "Attachment not found."));
        }

        // PageImages confirm once; the ink document re-confirms on every autosave to
        // re-stamp its size after the in-place overwrite.
        if (attachment.Status == SourceAttachmentStatus.Stored && attachment.Kind != SourceAttachmentKind.InkDocument)
        {
            return AppResult<SourceAttachment>.Fail(new AppError(409, "invalid_status", "Attachment has already been confirmed."));
        }

        var metadata = await _blobStorage.GetBlobMetadataAsync(attachment.BlobPath, ct);
        if (metadata is null)
        {
            return AppResult<SourceAttachment>.Fail(new AppError(400, "upload_not_found",
                "The file has not arrived in storage — upload it to the provided URL, then confirm."));
        }

        attachment.SizeBytes = metadata.SizeBytes;
        attachment.Status = SourceAttachmentStatus.Stored;
        attachment.UpdatedAt = DateTimeOffset.UtcNow;
        attachment = await _attachmentRepository.UpdateAsync(attachment, ct);

        // A changed derivation input invalidates whatever the worker derived before.
        if (DerivationKinds.Contains(attachment.Kind) && gate.Value!.DerivedText is not null)
        {
            await _sourceRepository.UpdateDerivedTextAsync(sourceId, null, ct);
        }

        return AppResult<SourceAttachment>.Success(attachment);
    }

    public async Task<AppResult<IReadOnlyList<SourceAttachmentWithUrl>>> ListAsync(
        Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId)
        {
            return AppResult<IReadOnlyList<SourceAttachmentWithUrl>>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Read access mirrors source read access: anyone who can see the source can see
        // its attachments. Visibility is enforced upstream by the source read the caller
        // performed; the world check above is the belt-and-suspenders.
        var attachments = (await _attachmentRepository.ListBySourceAsync(sourceId, ct))
            .Where(a => a.Status == SourceAttachmentStatus.Stored)
            .ToList();

        var result = new List<SourceAttachmentWithUrl>(attachments.Count);
        foreach (var attachment in attachments)
        {
            var url = await _blobStorage.GenerateDownloadSasUrlAsync(attachment.BlobPath, ct);
            result.Add(new SourceAttachmentWithUrl(attachment, url));
        }

        return AppResult<IReadOnlyList<SourceAttachmentWithUrl>>.Success(result);
    }

    public async Task<AppResult<bool>> DeleteAsync(
        Guid attachmentId, Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var gate = await LoadMutableSourceAsync(sourceId, worldId, actingUserId, role, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<bool>.Fail(gate.Error!);
        }

        var attachment = await _attachmentRepository.GetByIdAsync(attachmentId, ct);
        if (attachment is null || attachment.SourceId != sourceId)
        {
            return AppResult<bool>.Fail(new AppError(404, "not_found", "Attachment not found."));
        }

        // Blob first, failure swallowed (Library convention): an orphaned blob beats an
        // orphaned row pointing at nothing.
        try
        {
            await _blobStorage.DeleteBlobAsync(attachment.BlobPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blob delete failed for attachment {AttachmentId}; removing the row anyway", attachment.Id);
        }

        await _attachmentRepository.DeleteAsync(attachment.Id, ct);

        if (DerivationKinds.Contains(attachment.Kind) && gate.Value!.DerivedText is not null)
        {
            await _sourceRepository.UpdateDerivedTextAsync(sourceId, null, ct);
        }

        return AppResult<bool>.Success(true);
    }

    /// <summary>
    /// The write gate shared by request/confirm/delete: source exists in this world, the
    /// caller is its owner or a GM, and it has not entered the pipeline yet.
    /// </summary>
    private async Task<AppResult<Source>> LoadMutableSourceAsync(
        Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Observers cannot modify sources."));
        }

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        if (role != WorldRole.GM && source.CreatedByUserId != actingUserId)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Only the source's creator or a GM can modify its attachments."));
        }

        if (!MutableStatuses.Contains(source.ProcessingStatus))
        {
            return AppResult<Source>.Fail(new AppError(409, "invalid_status",
                $"Attachments cannot change while the source is {source.ProcessingStatus}."));
        }

        return AppResult<Source>.Success(source);
    }
}
