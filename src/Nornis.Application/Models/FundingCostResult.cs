using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Models;

/// <summary>What one payer covered (feature 39): <see cref="PaidBy"/> is a key's label, "Trial" or "Nornis".</summary>
public record FundingCostResult
{
    public required AiFundedBy Kind { get; init; }
    public required string PaidBy { get; init; }
    public required CostSummary Summary { get; init; }
}
