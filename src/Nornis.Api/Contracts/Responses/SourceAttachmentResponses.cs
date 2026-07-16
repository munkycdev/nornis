namespace Nornis.Api.Contracts.Responses;

public record SourceAttachmentResponse(
    Guid Id,
    Guid SourceId,
    string Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    int Ord,
    string Status,
    DateTimeOffset CreatedAt,
    string? Url = null);

public record SourceAttachmentUploadResponse(
    SourceAttachmentResponse Attachment,
    string UploadUrl);
