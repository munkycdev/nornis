using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <param name="Kind">
/// Restricts the result to facts or to relationships, and skips loading the other kind entirely.
/// Only worth setting when the caller genuinely wants one kind — a caller wanting a few of each
/// should make ONE request with <paramref name="FactLimit"/> and
/// <paramref name="RelationshipLimit"/> rather than two requests, because each request reloads
/// the world's artifacts.
/// </param>
/// <param name="Limit">
/// Most entries to return overall, applied last. Null means unlimited.
/// </param>
/// <param name="FactLimit">
/// Most facts to contribute, applied to the facts alone before they are merged with
/// relationships. Without it, a small overall <paramref name="Limit"/> over a fact-heavy world
/// returns no relationships at all.
/// </param>
/// <param name="RelationshipLimit">As <paramref name="FactLimit"/>, for relationships.</param>
/// <remarks>
/// Every cap here is applied AFTER visibility and truth-state filtering, so a cap can never
/// consume a slot on an entry the filters should have removed — which would silently drop a
/// visible entry from the result.
/// </remarks>
public record CanonQuery(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    TruthState? TruthState = null,
    CanonEntryKind? Kind = null,
    int? Limit = null,
    int? FactLimit = null,
    int? RelationshipLimit = null);
