using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// A single truth-state item in the Canon view — either an artifact fact or an artifact
/// relationship — enriched with the display names of the artifact(s) it concerns. Canon is a
/// flat, visibility-scoped projection; the UI groups entries into sections (confirmed world
/// state, rumors, disputed, hidden GM facts) and derives "recent changes" by <see cref="UpdatedAt"/>.
/// </summary>
public record CanonEntry(
    CanonEntryKind Kind,
    Guid Id,
    Guid ArtifactId,
    string ArtifactName,
    Guid? OtherArtifactId,
    string? OtherArtifactName,
    string Label,
    string? Detail,
    decimal? Confidence,
    TruthState TruthState,
    VisibilityScope Visibility,
    DateTimeOffset UpdatedAt);

public enum CanonEntryKind
{
    Fact,
    Relationship
}
