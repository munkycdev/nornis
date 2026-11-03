using Nornis.Domain.Models;

namespace Nornis.Application.Models;

public record ModelCostResult
{
    public required string Model { get; init; }

    /// <summary>The dollars are an estimate: the model isn't in the catalog (feature 39).</summary>
    public bool PriceEstimated { get; init; }
    public required CostSummary Summary { get; init; }
}
