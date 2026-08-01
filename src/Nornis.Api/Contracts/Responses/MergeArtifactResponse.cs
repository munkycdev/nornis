namespace Nornis.Api.Contracts.Responses;

/// <summary>The surviving artifact after a merge — the target the duplicate was folded into.</summary>
public record MergeArtifactResponse(Guid TargetArtifactId);
