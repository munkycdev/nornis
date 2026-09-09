using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Knowledge;

/// <summary>
/// Groups an already-filtered <see cref="ArtifactDetail"/> into the shape someone reading
/// about a person wants: what they carry, where they have been, who they answer to, what
/// they are caught up in, and who else is involved.
///
/// Pure, and deliberately not in the razor. Grouping is presentation-shaped, which is exactly
/// why it drifts when a component owns it — the continuity score was recomputed in the Web
/// for the same reason, and no compiler spans that boundary.
///
/// Takes a filtered detail rather than repositories on purpose: this type has no way to read
/// an unfiltered row, so it cannot become a second place where visibility is decided.
/// </summary>
public static class CharacterRecordProjector
{
    /// <summary>
    /// Per-type display cap. The remainder stays reachable through the artifact detail page,
    /// which is the view that exists to be exhaustive.
    /// </summary>
    public const int MaxPerGroup = 24;

    /// <summary>
    /// Fact display cap. A dossier is an orientation, not the artifact's full record.
    /// </summary>
    public const int MaxFacts = 60;

    /// <summary>
    /// Reading order for a person: what they hold, where they are, who they are bound to,
    /// what they are in the middle of, then everyone and everything else. Types absent from
    /// this list still render, after the named ones, in enum order.
    /// </summary>
    private static readonly ArtifactType[] GroupOrder =
    [
        ArtifactType.Item,
        ArtifactType.Location,
        ArtifactType.Faction,
        ArtifactType.Storyline,
        ArtifactType.Character
    ];

    public static CharacterRecord Project(ArtifactDetail detail)
    {
        var groups = detail.ConnectedArtifacts
            .GroupBy(a => a.Type)
            .Select(g => new CharacterRecordGroup(
                Type: g.Key,
                Artifacts: g.OrderBy(a => a.Name).Take(MaxPerGroup).ToList(),
                TotalCount: g.Count()))
            .OrderBy(g => OrderIndexOf(g.Type))
            .ThenBy(g => g.Type)
            .ToList();

        return new CharacterRecord(
            ArtifactId: detail.Artifact.Id,
            ArtifactName: detail.Artifact.Name,
            Summary: detail.Artifact.Summary,
            Facts: detail.Facts.Take(MaxFacts).ToList(),
            TotalFactCount: detail.Facts.Count,
            Groups: groups);
    }

    private static int OrderIndexOf(ArtifactType type)
    {
        var index = Array.IndexOf(GroupOrder, type);
        return index < 0 ? GroupOrder.Length : index;
    }
}
