namespace Nornis.Domain.Models;

public record GroupedCostSummary<TKey>
{
    public required TKey Key { get; init; }
    public required CostSummary Summary { get; init; }
}
