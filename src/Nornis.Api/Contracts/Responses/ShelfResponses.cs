namespace Nornis.Api.Contracts.Responses;

/// <summary>What a shelf link opens. No ids that lead anywhere else: the shelf is the whole surface.</summary>
public record ShelfResponse(
    string WorldName,
    string PlayerName,
    IReadOnlyList<ShelfDocumentResponse> Documents);

public record ShelfDocumentResponse(
    Guid Id,
    string Title,
    string Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? PageCount);

/// <summary>A standing link as the GM sees it. The code is the secret; this is a GM-only response.</summary>
public record ShelfLinkResponse(
    Guid Id,
    Guid PlayerId,
    string Code,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);
