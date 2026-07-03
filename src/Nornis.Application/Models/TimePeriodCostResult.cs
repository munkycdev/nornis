using Nornis.Domain.Models;

namespace Nornis.Application.Models;

public record TimePeriodCostResult
{
    public required CostSummary Today { get; init; }
    public required CostSummary ThisWeek { get; init; }
    public required CostSummary ThisMonth { get; init; }
    public required CostSummary AllTime { get; init; }
}
