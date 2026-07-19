namespace Nornis.Api.Contracts.Responses;

/// <summary>A completed reveal. <see cref="BatchId"/> is null when the request was entirely
/// no-ops (everything already party-visible), in which case no batch is minted.</summary>
public record RevealResponse(
    Guid? BatchId,
    int RevealedArtifacts,
    int RevealedFacts,
    int RevealedRelationships,
    int Corrections);

/// <summary>
/// 422 body: the reveal was rejected because the set was not reference-closed — a revealed
/// fact needs its artifact visible, a revealed relationship needs both endpoints visible.
/// Add <see cref="MissingArtifactIds"/> to the reveal and resubmit. Nothing was changed.
/// </summary>
public record RevealNotClosedResponse(
    string Code,
    string Message,
    IReadOnlyList<Guid> MissingArtifactIds);
