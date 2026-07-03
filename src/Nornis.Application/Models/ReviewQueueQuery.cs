using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record ReviewQueueQuery(
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole,
    Guid? FilterByBatchId = null);
