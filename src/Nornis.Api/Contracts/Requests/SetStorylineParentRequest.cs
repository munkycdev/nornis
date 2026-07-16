namespace Nornis.Api.Contracts.Requests;

/// <summary>Null clears the parent.</summary>
public record SetStorylineParentRequest(Guid? ParentArtifactId);
