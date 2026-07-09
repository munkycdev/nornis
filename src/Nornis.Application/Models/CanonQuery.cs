using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record CanonQuery(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    TruthState? TruthState = null);
