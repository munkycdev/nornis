namespace Nornis.Api.Contracts.Requests;

/// <param name="ClearStartedAt">
/// Null means "leave it" for every field here, so a date cannot be removed by sending null.
/// These two say so explicitly, the way <c>UpdateCharacterRequest.UnlinkArtifact</c> does.
/// </param>
public record UpdateCampaignRequest(
    string? Name = null,
    string? Description = null,
    string? Status = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null,
    bool ClearStartedAt = false,
    bool ClearEndedAt = false);
