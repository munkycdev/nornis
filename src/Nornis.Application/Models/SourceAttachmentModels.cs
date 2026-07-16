using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record RequestSourceAttachmentUploadCommand(
    Guid WorldId,
    Guid SourceId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string? FileName,
    string? ContentType,
    long SizeBytes,
    SourceAttachmentKind Kind,
    int Ord);

/// <summary>The pending attachment row plus the short-lived write SAS for the browser PUT.</summary>
public record SourceAttachmentUploadTicket(SourceAttachment Attachment, string UploadUrl);

/// <summary>An attachment with a short-lived read SAS minted for display/download.</summary>
public record SourceAttachmentWithUrl(SourceAttachment Attachment, string Url);
