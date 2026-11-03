namespace Nornis.Api.Contracts.Requests;

public record RequestLibraryUploadRequest(
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,
    string Visibility,
    Guid? AdventureId = null,
    // The Adventure this upload belongs to (feature 36); GM only.
    Guid? LinkedAdventureId = null);
