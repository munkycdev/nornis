using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ICostService
{
    Task<AppResult<TimePeriodCostResult>> GetSummaryAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        CancellationToken ct);

    Task<AppResult<IReadOnlyList<CampaignCostResult>>> GetByCampaignAsync(
        Guid userId,
        CancellationToken ct);

    Task<AppResult<IReadOnlyList<UserCostResult>>> GetByUserAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct);

    Task<AppResult<IReadOnlyList<OperationTypeCostResult>>> GetByOperationTypeAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct);

    Task<AppResult<IReadOnlyList<ModelCostResult>>> GetByModelAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct);
}
