using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>What a set of suggested ids resolves to on one artifact's detail.</summary>
public sealed record RevealPreselectionMatch(
    bool Artifact,
    IReadOnlyList<Guid> FactIds,
    IReadOnlyList<Guid> RelationshipIds);

/// <summary>
/// Resolves ids suggested by the convergence gauge against what is actually GM-only on an
/// artifact right now.
///
/// Matched rather than trusted, because the gauge is a snapshot: anything revealed between
/// reading it and opening the dialog is no longer revealable, and ticking a box for it would
/// put an id in the request that the reveal would reject. A rule rather than a line in a
/// lifecycle method — it is the contract between two features.
/// </summary>
public static class RevealPreselection
{
    public static RevealPreselectionMatch Match(
        ArtifactDetailDto detail, IReadOnlyCollection<Guid>? suggestedIds)
    {
        if (suggestedIds is null || suggestedIds.Count == 0)
        {
            return new RevealPreselectionMatch(false, [], []);
        }

        var artifact = false;
        var facts = new List<Guid>();
        var relationships = new List<Guid>();

        foreach (var id in suggestedIds)
        {
            if (id == detail.Id && detail.Visibility == "GMOnly")
            {
                artifact = true;
            }
            else if (detail.Facts.Any(f => f.Id == id && f.Visibility == "GMOnly"))
            {
                facts.Add(id);
            }
            else if (detail.Relationships.Any(r => r.Id == id && r.Visibility == "GMOnly"))
            {
                relationships.Add(id);
            }
        }

        return new RevealPreselectionMatch(artifact, facts, relationships);
    }
}
