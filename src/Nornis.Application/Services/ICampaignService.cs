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

    /// <summary>
    /// The campaign page's read model, assembled at the caller's visibility. Readable by any
    /// world member — a campaign is not an authorization boundary — but what it contains
    /// depends entirely on who is asking.
    /// </summary>
    Task<AppResult<CampaignDetail>> GetDetailAsync(
        Guid campaignId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    /// <summary>GM-only. Rewrites the world's campaign display order; returns it.</summary>
    Task<AppResult<IReadOnlyList<Campaign>>> ReorderAsync(
        Guid worldId, IReadOnlyList<Guid> orderedCampaignIds, WorldRole role, CancellationToken ct);

    Task<AppResult<Campaign>> UpdateAsync(UpdateCampaignCommand command, CancellationToken ct);

    Task<AppResult> DeleteAsync(Guid campaignId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Replaces the set of characters assigned to a campaign; returns the resulting characters.
    /// </summary>
    Task<AppResult<IReadOnlyList<Character>>> AssignCharactersAsync(AssignCampaignCharactersCommand command, CancellationToken ct);
}
