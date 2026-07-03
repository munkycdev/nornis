using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record MarkSourceReadyCommand(
    Guid SourceId,
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole);
