using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <param name="Kind">
/// Restricts the result to facts or to relationships. Also skips loading the other kind
/// entirely — the dashboard asks for three of each and used to receive the world's whole canon.
/// </param>
/// <param name="Limit">
/// Most entries to return, applied after visibility and truth-state filtering so a cap can never
/// hide an entry the filters would have kept. Null means unlimited.
/// </param>
public record CanonQuery(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    TruthState? TruthState = null,
    CanonEntryKind? Kind = null,
    int? Limit = null);
