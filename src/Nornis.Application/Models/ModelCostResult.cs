using Nornis.Domain.Models;

namespace Nornis.Application.Models;

public record ModelCostResult
{
    public required string Model { get; init; }
    public required CostSummary Summary { get; init; }
}
