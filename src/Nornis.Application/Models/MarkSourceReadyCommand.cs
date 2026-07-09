using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record MarkSourceReadyCommand(
    Guid SourceId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole);
