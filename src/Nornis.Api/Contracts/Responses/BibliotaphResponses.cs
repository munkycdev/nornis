namespace Nornis.Api.Contracts.Responses;

/// <summary>Profile › Bibliotaph. <c>Status</c> is none, connected or needs_reconnect.</summary>
public record BibliotaphConnectionResponse(
    bool Configured, string Status, DateTimeOffset? ConnectedAt, DateTimeOffset? LastUsedAt);

/// <summary>An address to send the browser to.</summary>
public record BibliotaphRedirectResponse(string Url);

// What Nornis provides to Bibliotaph's Nornis storage provider, under api/bibliotaph/v1. Named
// as the Bibliotaph integration spec names them: a field changes there first.

public record BibliotaphMeResponse(Guid UserId, string DisplayName);

public record BibliotaphProviderWorld(Guid WorldId, string Name, DateTimeOffset UpdatedAt);

public record BibliotaphProviderWorldsResponse(IReadOnlyList<BibliotaphProviderWorld> Worlds, string? NextCursor);

public record BibliotaphProviderFile(
    Guid FileId, string FileName, string ContentType, long SizeBytes, DateTimeOffset UpdatedAt, string Etag);

public record BibliotaphProviderFilesResponse(IReadOnlyList<BibliotaphProviderFile> Files, string? NextCursor);

public record BibliotaphFileLinkResponse(string Url, DateTimeOffset ExpiresAt);
