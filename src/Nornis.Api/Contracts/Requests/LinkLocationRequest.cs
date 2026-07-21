namespace Nornis.Api.Contracts.Requests;

/// <summary>Body for linking a session to a Location artifact.</summary>
public record LinkLocationRequest(Guid ArtifactId);
