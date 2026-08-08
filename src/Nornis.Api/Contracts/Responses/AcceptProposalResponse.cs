namespace Nornis.Api.Contracts.Responses;

/// <param name="MatchedExistingArtifact">
/// The Create was applied by binding to an artifact that already existed;
/// <paramref name="CreatedEntityId"/> is that artifact and nothing new was inserted.
/// </param>
/// <param name="CreatedMissingArtifactNames">
/// Artifacts the accept had to create first, because the proposal named them and neither
/// canon nor its batch held them. Empty on the ordinary path.
/// </param>
public record AcceptProposalResponse(
    Guid ProposalId,
    string Status,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId,
    Guid? CreatedEntityId,
    bool MatchedExistingArtifact = false,
    IReadOnlyList<string>? CreatedMissingArtifactNames = null);
