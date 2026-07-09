namespace Nornis.Api.Contracts.Requests;

public record CreateCampaignRequest(
    string Name,
    string? Description = null,
    string? Status = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null);
