namespace Nornis.Api.Contracts.Responses;

public record OperationTypeCostResponse(
    string OperationType,
    CostSummaryResponse Summary);
