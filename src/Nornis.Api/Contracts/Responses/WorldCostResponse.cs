namespace Nornis.Api.Contracts.Responses;

public record WorldCostResponse(
    Guid WorldId,
    string WorldName,
    CostSummaryResponse Summary);
