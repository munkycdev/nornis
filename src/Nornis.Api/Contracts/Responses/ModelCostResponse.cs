namespace Nornis.Api.Contracts.Responses;

/// <param name="PriceEstimated">The dollars are an estimate: a model the catalog doesn't price ran
/// (feature 39's "More from your key"), so it was priced at its provider's dearest catalog model.</param>
public record ModelCostResponse(
    string Model,
    CostSummaryResponse Summary,
    bool PriceEstimated = false);
