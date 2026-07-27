using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <param name="MatchedExistingArtifact">
/// True when this was a CreateArtifact that apply-time dedup bound to an artifact already
/// in canon. <paramref name="CreatedEntityId"/> then points at that pre-existing artifact,
/// and nothing new was inserted — the UI should say "matched", not "created".
/// </param>
public record AcceptProposalResult(
    Guid ProposalId,
    ReviewProposalStatus Status,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId,
    Guid? CreatedEntityId,
    bool MatchedExistingArtifact = false);
