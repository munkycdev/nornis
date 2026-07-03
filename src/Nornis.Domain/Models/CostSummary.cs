namespace Nornis.Domain.Models;

public record CostSummary
{
    public long TotalInputTokens { get; init; }
    public long TotalOutputTokens { get; init; }
    public long TotalTokens { get; init; }
    public decimal TotalEstimatedCostUsd { get; init; }
    public int OperationCount { get; init; }

    public static CostSummary Empty => new()
    {
        TotalInputTokens = 0,
        TotalOutputTokens = 0,
        TotalTokens = 0,
        TotalEstimatedCostUsd = 0m,
        OperationCount = 0
    };
}
