namespace Nornis.Application.Models;

/// <summary>
/// A heuristic quality score for a world's recorded memory, computed from the knowledge
/// graph (no AI). Each metric is 0–100. When the world has no artifacts yet there is nothing
/// to measure, so <see cref="HasData"/> is false and scores are zero.
/// </summary>
public record WorldHealth(
    bool HasData,
    int OverallScore,
    string Label,
    int Consistency,
    int Completeness,
    int Groundedness,
    int Recency,
    int ArtifactCount,
    int StatementCount);
