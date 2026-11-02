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
    DateTimeOffset UpdatedAt,
    string? Slug = null,
    Guid? AdventureId = null,
    string Origin = "Upload",
    bool InBibliotaph = false,
    bool HasCover = false);

/// <summary>The Library's Bibliotaph strip (feature 31). <c>State</c> is a <c>BibliotaphShelfStates</c> value.</summary>
public record LibraryBibliotaphResponse(string State, string? OwnerName);

public record LibraryUploadResponse(
    LibraryDocumentResponse Document,
    string UploadUrl);

public record LibraryDownloadResponse(
    string DownloadUrl,
    string FileName,
    string ContentType,
    long SizeBytes);
