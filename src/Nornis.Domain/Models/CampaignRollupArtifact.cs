using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// An artifact the campaign's sources evidence, with how much of the campaign it appears in.
///
/// Campaign membership is <em>derived</em>, never stamped: an artifact belongs to a campaign
/// because the campaign's sources cite it, through any of the three citable targets — the
/// artifact itself, one of its facts, or a relationship it is an end of. Nothing declares it,
/// so nothing can go stale.
/// </summary>
/// <param name="SourceCount">
/// Distinct sources in this campaign that evidence the artifact. The ranking signal: an NPC
/// cited by nine sessions outranks one mentioned once, which is the difference between the
/// campaign's cast and its footnotes.
/// </param>
public record CampaignRollupArtifact(
    Guid ArtifactId,
    string Name,
    ArtifactType Type,
    string? Summary,
    ArtifactStatus Status,
    int SourceCount,
    string? Slug = null);

/// <summary>
/// One page of the rollup. <paramref name="TotalCount"/> is the unbounded truth so the UI can
/// say how much it is not showing — a truncated list that looks complete is worse than a short
/// one that admits it.
/// </summary>
public record CampaignRollup(
    IReadOnlyList<CampaignRollupArtifact> Artifacts,
    int TotalCount);
