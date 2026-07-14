namespace Nornis.Api.Contracts.Requests;

/// <summary>The duplicate artifact to fold into the target named in the route.</summary>
public record MergeArtifactRequest(Guid SourceArtifactId);
