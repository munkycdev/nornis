namespace Nornis.Api.Contracts.Responses;

/// <param name="MatchedExistingArtifact">
/// The Create was applied by binding to an artifact that already existed;
/// <paramref name="CreatedEntityId"/> is that artifact and nothing new was inserted.
/// </param>
public record AcceptProposalResponse(
    Guid ProposalId,
    string Status,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId,
    Guid? CreatedEntityId,
    bool MatchedExistingArtifact = false);
