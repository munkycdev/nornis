namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// A campaign as the public world shows it. Its own shape rather than
/// <see cref="CampaignDetailResponse"/>: that one carries the GM's unfiled-sources offer and
/// full character records, and a field a response cannot carry is a field that cannot leak.
/// </summary>
public record PublicCampaignDetailResponse(
    CampaignResponse Campaign,
    IReadOnlyList<PublicCampaignCastResponse> Cast,
    IReadOnlyList<CampaignArtifactResponse> Artifacts,
    int ArtifactTotalCount,
    IReadOnlyList<SourceListItemResponse> RecentSessions,
    int SessionCount,
    DateTimeOffset? FirstSessionAt,
    DateTimeOffset? LastSessionAt,
    PublicCampaignRecapResponse Recap);

/// <summary>A character in the cast: a name and who plays them, nothing of the sheet.</summary>
public record PublicCampaignCastResponse(Guid Id, string Name, string PlayerName);

/// <summary>The party rendering of the recap, and only that.</summary>
public record PublicCampaignRecapResponse(bool HasData, DateTimeOffset? GeneratedAt, string? Content);
