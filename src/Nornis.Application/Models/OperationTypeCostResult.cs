using Nornis.Domain.Models;

namespace Nornis.Application.Models;

public record OperationTypeCostResult
{
    public required string OperationType { get; init; }
    public required CostSummary Summary { get; init; }
}
