namespace Nornis.Api.Contracts.Responses;

public record LibraryDocumentResponse(
    Guid Id,
    Guid WorldId,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,
    string Visibility,
    string Status,
    int? PageCount,
    int ChunkCount,
    string? ErrorMessage,
    Guid UploadedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record LibraryUploadResponse(
    LibraryDocumentResponse Document,
    string UploadUrl);

public record LibraryDownloadResponse(
    string DownloadUrl,
    string FileName,
    string ContentType,
    long SizeBytes);
