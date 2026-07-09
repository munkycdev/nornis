using Nornis.Domain.Models;

namespace Nornis.Application.Models;

public record WorldCostResult
{
    public required Guid WorldId { get; init; }
    public required string WorldName { get; init; }
    public required CostSummary Summary { get; init; }
}
