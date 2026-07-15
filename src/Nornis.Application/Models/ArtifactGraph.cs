namespace Nornis.Application.Models;

/// <summary>
/// The caller-visible world graph: artifacts as nodes, relationships (between visible
/// artifacts) as edges. Small enough at current scale to ship whole; focus and depth
/// are client-side concerns.
/// </summary>
public record ArtifactGraph(
    IReadOnlyList<ArtifactGraphNode> Nodes,
    IReadOnlyList<ArtifactGraphEdge> Edges);

public record ArtifactGraphNode(Guid Id, string Name, string Type, string Status);

public record ArtifactGraphEdge(Guid Id, Guid SourceId, Guid TargetId, string Type);
