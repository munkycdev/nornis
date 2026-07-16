namespace Nornis.Api.Contracts.Responses;

/// <summary>The heuristic tier of Continuity Health: four 0–100 components and their inputs.</summary>
public record WorldHealthResponse(
    bool HasData,
    int OverallScore,
    string Label,
    int Consistency,
    int Completeness,
    int Groundedness,
    int Recency,
    int ArtifactCount,
    int StatementCount);
