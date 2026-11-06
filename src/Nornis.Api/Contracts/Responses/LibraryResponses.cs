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
    bool HasCover = false,
    // Feature 36: decided by LibraryCategories, so no client decides them differently.
    string Category = "Other",
    Guid? LinkedAdventureId = null,
    bool OnShelf = true,
    // Feature 40: a print of an Adventure that drew on the web, so sharing it warns (GMs only).
    bool ResearchedOnWeb = false);

/// <summary>Where opening a book sends the browser: Bibliotaph's reader, or a download.</summary>
public record LibraryReadResponse(string Url, string Kind);

/// <summary>One open of a Bibliotaph book, for the GM.</summary>
public record LibraryOpenResponse(string ViewerName, DateTimeOffset OpenedAt, bool FromShelfLink);

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
