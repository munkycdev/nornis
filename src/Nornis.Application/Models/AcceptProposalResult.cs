using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <param name="MatchedExistingArtifact">
/// True when this was a CreateArtifact that apply-time dedup bound to an artifact already
/// in canon. <paramref name="CreatedEntityId"/> then points at that pre-existing artifact,
/// and nothing new was inserted — the UI should say "matched", not "created".
/// </param>
/// <param name="CreatedMissingArtifactNames">
/// Artifacts this accept had to create before it could apply, because the proposal named them
/// and neither canon nor the batch held them. Empty on the ordinary path. The reviewer is told:
/// an accept that quietly grows canon by more than the card in front of them is the kind of
/// thing they should hear about.
/// </param>
public record AcceptProposalResult(
    Guid ProposalId,
    ReviewProposalStatus Status,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId,
    Guid? CreatedEntityId,
    bool MatchedExistingArtifact = false,
    IReadOnlyList<string>? CreatedMissingArtifactNames = null);
