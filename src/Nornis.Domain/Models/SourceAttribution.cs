using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// The minimum needed to decide whether a caller may see a source, and to name it if they may.
///
/// Exists so provenance display does not have to load whole <c>Source</c> rows. A source carries
/// <c>Body</c> and <c>DerivedText</c> — the full session transcript and any machine-derived text,
/// both unbounded — and a well-cited artifact is referenced by a dozen or more of them. Loading
/// those rows to read a title pulls megabytes across the wire to render a few words.
/// </summary>
/// <param name="Visibility">Scope of the source itself, not of what it cites.</param>
/// <param name="CreatedByUserId">
/// The source's author, needed because Private sources are visible to their owner.
/// </param>
public record SourceAttribution(
    Guid Id,
    string Title,
    VisibilityScope Visibility,
    Guid CreatedByUserId);
