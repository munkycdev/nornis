using Nornis.Domain.Entities;

namespace Nornis.Application.Models;

/// <summary>
/// Aggregated read model for an artifact detail view: the artifact itself plus its
/// visible facts, relationships, the counterpart artifacts those relationships connect to,
/// and the supporting source references. All collections are already visibility-filtered
/// for the requesting user's role. <paramref name="PlayedBy"/> carries the display names
/// of members whose Character records link to this artifact (Character artifacts only).
/// <paramref name="DeclaredCampaigns"/> are the campaigns a GM has declared this storyline
/// to belong to (Storyline artifacts only; empty otherwise).
/// </summary>
public record ArtifactDetail(
    Artifact Artifact,
    IReadOnlyList<ArtifactFact> Facts,
    IReadOnlyList<ArtifactRelationship> Relationships,
    IReadOnlyList<Artifact> ConnectedArtifacts,
    IReadOnlyList<SourceReference> SourceReferences,
    IReadOnlyDictionary<Guid, string> SourceTitles,
    IReadOnlyList<string> PlayedBy,
    IReadOnlyList<Campaign> DeclaredCampaigns);
