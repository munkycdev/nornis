namespace Nornis.Domain.Models;

/// <summary>
/// A by-model cost row's key: the model as sent on the wire, and whether its dollars are an
/// estimate, because it ran a model the catalog doesn't price (feature 39's "More from your key").
/// </summary>
public sealed record ModelCostKey(string Model, bool PriceEstimated);
