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

    /// <summary>
    /// GM-only. Makes this the campaign the world is playing now — the one a capture belongs
    /// to unless the GM says otherwise. Refuses a campaign that is not Active, which is what
    /// lets every reader trust that the world's current campaign is a live one.
    /// </summary>
    Task<AppResult<Campaign>> SetCurrentAsync(
        Guid campaignId, Guid worldId, WorldRole role, CancellationToken ct);

    /// <summary>GM-only. Leaves the world with no current campaign, so captures default to
    /// none until one is chosen again.</summary>
    Task<AppResult> ClearCurrentAsync(Guid worldId, WorldRole role, CancellationToken ct);

    Task<AppResult> DeleteAsync(Guid campaignId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Replaces the set of characters assigned to a campaign; returns the resulting characters.
    /// </summary>
    Task<AppResult<IReadOnlyList<Character>>> AssignCharactersAsync(AssignCampaignCharactersCommand command, CancellationToken ct);

    /// <summary>
    /// GM-only. Files sources that have no campaign under this one — the confirmation of
    /// what <see cref="GetDetailAsync"/> offered as <see cref="CampaignDetail.UnfiledInSpan"/>.
    /// Every id must be an unfiled source of this world; a source already filed anywhere,
    /// this campaign included, is refused rather than moved. Returns how many were filed.
    /// </summary>
    Task<AppResult<int>> FileSourcesAsync(FileCampaignSourcesCommand command, CancellationToken ct);
}
