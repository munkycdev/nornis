namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// The maintained world digest, rendered for the caller. GMs get <c>Content</c> = the GM
/// digest and <c>PartyPreview</c> = the party recap they can check before sharing a screen;
/// everyone else gets <c>Content</c> = the party recap and no preview. When the world has
/// never been digested, <c>HasData</c> is false and everything else is null.
/// </summary>
public record WorldDigestResponse(
    bool HasData,
    DateTimeOffset? GeneratedAt,
    string? Content,
    string? PartyPreview);
