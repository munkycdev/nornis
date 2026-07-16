namespace Nornis.Api.Contracts.Responses;

public record WorldResponse(
    Guid Id,
    string Name,
    string? Description,
    string? GameSystem,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? MyRole = null,
    decimal? DailyAiBudgetUsd = null,
    string? PublicSlug = null,
    bool PublicAccessEnabled = false);
