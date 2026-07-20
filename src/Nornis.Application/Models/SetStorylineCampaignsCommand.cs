using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Replaces the set of campaigns a storyline is declared to belong to. An empty
/// <paramref name="CampaignIds"/> clears every declaration, leaving only the campaigns
/// derived from the sessions that touched the storyline.
/// </summary>
public record SetStorylineCampaignsCommand(
    Guid ArtifactId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    IReadOnlyList<Guid> CampaignIds);
