namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// Replaces the full set of characters assigned to a campaign.
/// </summary>
public record AssignCampaignCharactersRequest(
    IReadOnlyCollection<Guid> CharacterIds);
