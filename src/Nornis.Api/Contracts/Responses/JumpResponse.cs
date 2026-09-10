namespace Nornis.Api.Contracts.Responses;

public record JumpResponse(IReadOnlyList<JumpGroupResponse> Groups);

/// <param name="Kind">Campaign, Character, Artifact, Session or Library.</param>
/// <param name="TotalCount">Visible matches of this kind before the per-kind cap.</param>
public record JumpGroupResponse(string Kind, IReadOnlyList<JumpItemResponse> Items, int TotalCount);

public record JumpItemResponse(Guid Id, string Name, string? Detail);
