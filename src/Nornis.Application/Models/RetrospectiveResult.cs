namespace Nornis.Application.Models;

/// <summary>
/// Outcome of a storyline retrospective: how many Active storylines were assessed and
/// how many closure proposals were created (zero when everything is still in motion).
/// </summary>
public record RetrospectiveResult(
    int AssessedCount,
    int ProposedCount,
    Guid? ReviewBatchId);
