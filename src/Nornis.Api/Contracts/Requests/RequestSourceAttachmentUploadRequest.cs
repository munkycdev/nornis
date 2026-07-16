namespace Nornis.Api.Contracts.Requests;

public record RequestSourceAttachmentUploadRequest(
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,
    int Ord = 0);
