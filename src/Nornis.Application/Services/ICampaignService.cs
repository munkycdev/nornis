using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ICampaignService
{
    Task<AppResult<Campaign>> CreateAsync(CreateCampaignCommand command, CancellationToken ct);

    Task<AppResult<Campaign>> GetByIdAsync(Guid campaignId, Guid worldId, CancellationToken ct);

    Task<AppResult<IReadOnlyList<Campaign>>> ListByWorldAsync(Guid worldId, CancellationToken ct);

    Task<AppResult<Campaign>> UpdateAsync(UpdateCampaignCommand command, CancellationToken ct);

    Task<AppResult> DeleteAsync(Guid campaignId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Replaces the set of characters assigned to a campaign; returns the resulting characters.
    /// </summary>
    Task<AppResult<IReadOnlyList<Character>>> AssignCharactersAsync(AssignCampaignCharactersCommand command, CancellationToken ct);
}
