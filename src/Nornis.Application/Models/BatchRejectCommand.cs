using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record BatchRejectCommand(
    IReadOnlyList<Guid> ProposalIds,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole);
