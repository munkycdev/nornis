namespace Nornis.Api.Contracts.Responses;

public record ArtifactGraphResponse(
    IReadOnlyList<ArtifactGraphNodeResponse> Nodes,
    IReadOnlyList<ArtifactGraphEdgeResponse> Edges);

public record ArtifactGraphNodeResponse(Guid Id, string Name, string Type, string Status);

public record ArtifactGraphEdgeResponse(Guid Id, Guid SourceId, Guid TargetId, string Type);
