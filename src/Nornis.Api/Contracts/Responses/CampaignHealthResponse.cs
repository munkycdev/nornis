namespace Nornis.Api.Contracts.Responses;

public record CampaignHealthResponse(
    bool HasData,
    int OverallScore,
    string Label,
    int Consistency,
    int Completeness,
    int Groundedness,
    int Recency,
    int ArtifactCount,
    int StatementCount);
