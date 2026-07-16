namespace Nornis.Api.Contracts.Requests;

public record RequestLibraryUploadRequest(
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,
    string Visibility);
