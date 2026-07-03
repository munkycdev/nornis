using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ISourceService
{
    Task<AppResult<Source>> CreateAsync(CreateSourceCommand command, CancellationToken ct);
    Task<AppResult<Source>> GetByIdAsync(Guid sourceId, Guid campaignId, Guid requestingUserId, CampaignRole role, CancellationToken ct);
    Task<AppResult<Source>> UpdateAsync(UpdateSourceCommand command, CancellationToken ct);
    Task<AppResult> DeleteAsync(Guid sourceId, Guid campaignId, Guid actingUserId, CampaignRole role, CancellationToken ct);
    Task<AppResult<IReadOnlyList<Source>>> ListByCampaignAsync(Guid campaignId, Guid requestingUserId, CampaignRole role, CancellationToken ct);
    Task<AppResult<Source>> MarkReadyAsync(MarkSourceReadyCommand command, CancellationToken ct);
}
