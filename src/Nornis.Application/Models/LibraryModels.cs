using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record RequestLibraryUploadCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    LibraryDocumentKind Kind,
    VisibilityScope Visibility);

/// <summary>The pending document plus the short-lived SAS URL the browser PUTs the bytes to.</summary>
public record LibraryUploadTicket(LibraryDocument Document, string UploadUrl);

/// <summary>A fresh read-SAS for a document, minted per request — never persisted.</summary>
public record LibraryDownload(string DownloadUrl, string FileName, string ContentType, long SizeBytes);
