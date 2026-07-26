namespace Nornis.Api.Contracts.Requests;

/// <summary>Body for pinning an existing Location artifact on a source's map.</summary>
public record CreatePlacemarkRequest(Guid ArtifactId);
