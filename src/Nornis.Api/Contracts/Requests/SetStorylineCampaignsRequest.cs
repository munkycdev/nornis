namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// Replaces the storyline's declared campaign set. An empty or null list clears every
/// declaration.
/// </summary>
public record SetStorylineCampaignsRequest(IReadOnlyList<Guid>? CampaignIds);
