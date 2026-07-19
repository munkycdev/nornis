using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// A GM's request to reveal a curated set of GM-only knowledge to the party. Artifacts,
/// facts, and relationships named here are promoted <c>GMOnly → PartyVisible</c>; corrections
/// re-truth-state existing party-visible beliefs the reveal supersedes. Elements already
/// party-visible are no-ops; <c>Private</c> elements are rejected (reveal promotes GM-only
/// knowledge only).
/// </summary>
public record RevealCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    IReadOnlyList<Guid> ArtifactIds,
    IReadOnlyList<Guid> FactIds,
    IReadOnlyList<Guid> RelationshipIds,
    IReadOnlyList<FactCorrection> Corrections,
    string? Note);

/// <summary>A belief-correction applied in the same reveal: change an existing fact's
/// <see cref="TruthState"/> (e.g. to <c>Disputed</c>/<c>False</c>) as the reveal supersedes
/// it. The fact is re-stated, never deleted.</summary>
public record FactCorrection(Guid FactId, TruthState TruthState);

/// <summary>
/// Outcome of a reveal. When <see cref="MissingArtifactIds"/> is non-empty the reveal was
/// <em>not</em> applied — the set was not reference-closed and those artifacts must be added
/// (a revealed fact needs its artifact visible; a revealed relationship needs both endpoints
/// visible). Otherwise the counts describe what was promoted and <see cref="BatchId"/> is the
/// reveal batch.
/// </summary>
public record RevealResult(
    Guid? BatchId,
    int RevealedArtifacts,
    int RevealedFacts,
    int RevealedRelationships,
    int Corrections,
    IReadOnlyList<Guid> MissingArtifactIds)
{
    public bool IsClosed => MissingArtifactIds.Count == 0;

    public int TotalRevealed => RevealedArtifacts + RevealedFacts + RevealedRelationships;
}

/// <summary>Outcome of revealing a source: it is now party-visible (or already was). Attachments
/// such as a map image ride the source's visibility, so revealing the source surfaces them.</summary>
public record RevealSourceResult(Guid SourceId, string Title, bool WasAlreadyVisible);
