using System.Linq.Expressions;
using Nornis.Domain.Entities;
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

    // The SQL forms of CanSee, one per queried entity. EF must see concrete property
    // access to translate, and the entities share no visibility interface, so the
    // predicate repeats per type HERE — and only here, where the copies sit side by
    // side and a drifted arm is visible in one screenful. Repositories must use these
    // rather than inlining the predicate: a hand-rolled copy in a query is how a
    // Private row ends up in someone else's count. Locals are hoisted so EF renders
    // them as SQL parameters.

    public Expression<Func<Artifact, bool>> CanSeeArtifact()
    {
        var scopes = Scopes;
        var owner = PrivateOwnerUserId;
        return a => scopes.Contains(a.Visibility)
            && (a.Visibility != VisibilityScope.Private || owner == null || a.CreatedByUserId == owner);
    }

    public Expression<Func<ArtifactFact, bool>> CanSeeFact()
    {
        var scopes = Scopes;
        var owner = PrivateOwnerUserId;
        return f => scopes.Contains(f.Visibility)
            && (f.Visibility != VisibilityScope.Private || owner == null || f.CreatedByUserId == owner);
    }

    public Expression<Func<ArtifactRelationship, bool>> CanSeeRelationship()
    {
        var scopes = Scopes;
        var owner = PrivateOwnerUserId;
        return r => scopes.Contains(r.Visibility)
            && (r.Visibility != VisibilityScope.Private || owner == null || r.CreatedByUserId == owner);
    }

    public Expression<Func<Source, bool>> CanSeeSource()
    {
        var scopes = Scopes;
        var owner = PrivateOwnerUserId;
        return s => scopes.Contains(s.Visibility)
            && (s.Visibility != VisibilityScope.Private || owner == null || s.CreatedByUserId == owner);
    }
}
