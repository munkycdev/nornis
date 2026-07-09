using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record ReviewQueueQuery(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    Guid? FilterByBatchId = null);
