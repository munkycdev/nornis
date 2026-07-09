using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record BatchAcceptCommand(
    IReadOnlyList<Guid> ProposalIds,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole);
