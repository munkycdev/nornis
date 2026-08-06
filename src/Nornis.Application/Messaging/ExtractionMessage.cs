namespace Nornis.Application.Messaging;

/// <summary>
/// What a message on the source-extraction queue asks the worker to do. Old messages
/// serialized without a Kind deserialize to the default, <see cref="Extraction"/>.
/// </summary>
public enum ExtractionKind
{
    Extraction,
    RelationshipBackfill,
    SummaryRefresh
}

/// <param name="SourceId">
/// The source to process. Empty — and only for <see cref="ExtractionKind.SummaryRefresh"/>
/// messages, which are about an artifact, not a source; the worker's validation gate is
/// kind-aware about exactly this.
/// </param>
/// <param name="ArtifactId">
/// The artifact whose summary to refresh; set only on SummaryRefresh messages.
/// </param>
/// <param name="RequestedAt">
/// When the refresh was requested. The staleness gate: an artifact whose
/// SummaryRefreshedAt is already past this was regenerated against newer state, and the
/// queued duplicate is skipped instead of re-bought. Null (an old or hand-sent message)
/// means no gate.
/// </param>
public record ExtractionMessage(
    Guid SourceId,
    Guid WorldId,
    ExtractionKind Kind = ExtractionKind.Extraction,
    Guid? ArtifactId = null,
    DateTimeOffset? RequestedAt = null);
