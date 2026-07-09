using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ICostService
{
    Task<AppResult<TimePeriodCostResult>> GetSummaryAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        CancellationToken ct);

    Task<AppResult<IReadOnlyList<WorldCostResult>>> GetByWorldAsync(
        Guid userId,
        CancellationToken ct);

    Task<AppResult<IReadOnlyList<UserCostResult>>> GetByUserAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct);

    Task<AppResult<IReadOnlyList<OperationTypeCostResult>>> GetByOperationTypeAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct);

    Task<AppResult<IReadOnlyList<ModelCostResult>>> GetByModelAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct);
}
