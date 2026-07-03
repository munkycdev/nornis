namespace Nornis.Api.Contracts.Responses;

public record UserCostResponse(
    Guid UserId,
    string Username,
    CostSummaryResponse Summary);
