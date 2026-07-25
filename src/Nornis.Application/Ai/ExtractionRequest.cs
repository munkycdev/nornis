using Nornis.Application.Knowledge;

namespace Nornis.Application.Ai;

public class ExtractionRequest
{
    public required string SourceBody { get; init; }
    public required string SourceTitle { get; init; }
    public required string SourceType { get; init; }
    public required string SourceVisibility { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public string? CampaignName { get; init; }
    public string? CampaignStatus { get; init; }
    public IReadOnlyList<ArtifactContext> ExistingArtifacts { get; init; } = [];

    /// <summary>Party/GM-visible published reference passages retrieved from the world's
    /// Library to ground extraction. Empty when the world has no indexed documents in scope.</summary>
    public IReadOnlyList<KnowledgePassage> ReferencePassages { get; init; } = [];

    /// <summary>The party's last known location(s), inferred from the nearest prior timeline
    /// source with accepted Location links. Null when no prior source in scope establishes one.</summary>
    public RecentLocationContext? RecentLocations { get; init; }
}
