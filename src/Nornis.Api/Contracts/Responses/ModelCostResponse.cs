namespace Nornis.Api.Contracts.Responses;

public record ModelCostResponse(
    string Model,
    CostSummaryResponse Summary);
