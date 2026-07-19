using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// Pure reference-closure check for a reveal. A reveal is coherent only when, once applied,
/// no party-visible element points at a GM-only one. Only <em>artifacts</em> can be missing
/// dependencies: a revealed fact needs its parent artifact visible, and a revealed
/// relationship needs both endpoint artifacts visible — nothing needs a fact or a
/// relationship, so a single pass suffices (revealing an artifact adds no new obligations).
/// </summary>
public static class RevealClosure
{
    /// <summary>
    /// Returns the artifact ids that must also be revealed for the set to be reference-closed,
    /// in deterministic order (fact parents first, then relationship endpoints), de-duplicated.
    /// Empty means the set is closed.
    /// </summary>
    /// <param name="revealArtifactIds">Artifacts being promoted GMOnly → PartyVisible.</param>
    /// <param name="revealFactParentArtifactIds">Parent artifact of each fact being promoted.</param>
    /// <param name="revealRelationshipEndpoints">Both endpoints of each relationship being promoted.</param>
    /// <param name="knownArtifactVisibility">Current visibility of every referenced artifact.</param>
    public static IReadOnlyList<Guid> MissingArtifactDependencies(
        IReadOnlyCollection<Guid> revealArtifactIds,
        IReadOnlyCollection<Guid> revealFactParentArtifactIds,
        IReadOnlyCollection<(Guid ArtifactAId, Guid ArtifactBId)> revealRelationshipEndpoints,
        IReadOnlyDictionary<Guid, VisibilityScope> knownArtifactVisibility)
    {
        // Artifacts that will be party-visible after the reveal: those being promoted, plus
        // any already party-visible.
        var willBeVisible = new HashSet<Guid>(revealArtifactIds);
        foreach (var (id, visibility) in knownArtifactVisibility)
        {
            if (visibility == VisibilityScope.PartyVisible)
            {
                willBeVisible.Add(id);
            }
        }

        var missing = new List<Guid>();
        var seen = new HashSet<Guid>();

        void Require(Guid artifactId)
        {
            if (!willBeVisible.Contains(artifactId) && seen.Add(artifactId))
            {
                missing.Add(artifactId);
            }
        }

        foreach (var artifactId in revealFactParentArtifactIds)
        {
            Require(artifactId);
        }

        foreach (var (aId, bId) in revealRelationshipEndpoints)
        {
            Require(aId);
            Require(bId);
        }

        return missing;
    }
}
