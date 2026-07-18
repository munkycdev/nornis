using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// What a reader may see. Non-Private rows are gated by <see cref="Scopes"/> alone.
/// Private rows are additionally gated by ownership: <see cref="PrivateOwnerUserId"/>
/// null means unrestricted (GM or internal integrity scans); otherwise only rows
/// created by that user match. Private rows with a NULL CreatedByUserId
/// (unattributable legacy rows, GM-authored PartOf links) are visible only to
/// unrestricted readers — fail closed.
/// </summary>
public sealed record VisibilityFilter
{
    public required IReadOnlyList<VisibilityScope> Scopes { get; init; }

    public Guid? PrivateOwnerUserId { get; init; }

    /// <summary>GM-equivalent: every scope, unrestricted Private. Also used by GM-gated
    /// internal scans (health, continuity audit, review name-resolution, cycle checks).</summary>
    public static readonly VisibilityFilter All = new()
    {
        Scopes = [VisibilityScope.PartyVisible, VisibilityScope.GMOnly, VisibilityScope.Private]
    };

    public static VisibilityFilter ForRole(WorldRole role, Guid userId) => role switch
    {
        WorldRole.GM => All,
        WorldRole.Player => new()
        {
            Scopes = [VisibilityScope.PartyVisible, VisibilityScope.Private],
            PrivateOwnerUserId = userId
        },
        _ => new() { Scopes = [VisibilityScope.PartyVisible] } // Observer and unknown
    };

    /// <summary>
    /// AI context assembly for a source: readers of the produced proposals are the
    /// source's readers, so context must be limited to what they may already see.
    /// </summary>
    public static VisibilityFilter ForSourceContext(VisibilityScope sourceVisibility, Guid sourceCreatedByUserId) =>
        sourceVisibility switch
        {
            VisibilityScope.Private => new()
            {
                Scopes = [VisibilityScope.Private],
                PrivateOwnerUserId = sourceCreatedByUserId
            },
            VisibilityScope.GMOnly => new()
            {
                Scopes = [VisibilityScope.GMOnly, VisibilityScope.PartyVisible]
            },
            _ => new() { Scopes = [VisibilityScope.PartyVisible] }
        };

    public bool CanSee(VisibilityScope visibility, Guid? createdByUserId) =>
        Scopes.Contains(visibility)
        && (visibility != VisibilityScope.Private
            || PrivateOwnerUserId is null
            || createdByUserId == PrivateOwnerUserId);
}
