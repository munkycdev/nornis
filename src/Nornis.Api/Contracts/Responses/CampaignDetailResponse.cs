namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// Everything the campaign page shows, at the caller's visibility. Members of any role may
/// read it — a campaign is a place to look, not an authorization boundary — but what it
/// carries depends on who asked.
/// </summary>
public record CampaignDetailResponse(
    CampaignResponse Campaign,
    IReadOnlyList<CharacterResponse> Characters,
    IReadOnlyList<CampaignArtifactResponse> Artifacts,
    int ArtifactTotalCount,
    IReadOnlyList<SourceListItemResponse> RecentSessions,
    int SessionCount,
    DateTimeOffset? FirstSessionAt,
    DateTimeOffset? LastSessionAt,
    CampaignRecapResponse Recap,
    UnfiledSourcesResponse UnfiledInSpan);

/// <summary>
/// Sources under no campaign whose date falls inside this campaign's declared dates — what
/// the page offers a GM to file here. Empty for everyone but a GM, and for an undated
/// campaign. <paramref name="Items"/> is the first page of <paramref name="TotalCount"/>.
/// </summary>
public record UnfiledSourcesResponse(
    IReadOnlyList<SourceListItemResponse> Items,
    int TotalCount);

/// <summary>
/// An artifact this campaign's sources evidence. <paramref name="SourceCount"/> is how many
/// of them do — the difference between the campaign's cast and a passing mention.
/// </summary>
public record CampaignArtifactResponse(
    Guid Id,
    string Name,
    string Type,
    string? Summary,
    string Status,
    int SourceCount,
    string? Slug = null);

/// <summary>
/// The generated "story so far", rendered for the caller — same shape and same
/// HasData=false convention as <see cref="WorldDigestResponse"/>.
/// </summary>
public record CampaignRecapResponse(
    bool HasData,
    DateTimeOffset? GeneratedAt,
    string? Content,
    string? PartyPreview);
