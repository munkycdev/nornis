namespace Nornis.Api.Contracts.Responses;

public record CostSummaryResponse(
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalTokens,
    decimal TotalEstimatedCostUsd,
    int OperationCount);
