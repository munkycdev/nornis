namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// The sources to file under a campaign — the GM's confirmed selection from what the
/// campaign page offered. Each must currently be filed under no campaign.
/// </summary>
public record FileCampaignSourcesRequest(
    IReadOnlyList<Guid> SourceIds);
