namespace Nornis.Application.Models;

/// <summary>
/// A heuristic quality score for a campaign's recorded memory, computed from the knowledge
/// graph (no AI). Each metric is 0–100. When the campaign has no artifacts yet there is nothing
/// to measure, so <see cref="HasData"/> is false and scores are zero.
/// </summary>
public record CampaignHealth(
    bool HasData,
    int OverallScore,
    string Label,
    int Consistency,
    int Completeness,
    int Groundedness,
    int Recency,
    int ArtifactCount,
    int StatementCount);
