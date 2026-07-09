using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record AddMemberCommand(
    Guid WorldId,
    Guid TargetUserId,
    WorldRole Role,
    Guid ActingUserId);
