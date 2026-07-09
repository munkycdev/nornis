using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record UpdateMemberRoleCommand(
    Guid WorldId,
    Guid TargetUserId,
    WorldRole NewRole,
    Guid ActingUserId);
