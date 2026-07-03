using Nornis.Domain.Models;

namespace Nornis.Application.Models;

public record UserCostResult
{
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required CostSummary Summary { get; init; }
}
