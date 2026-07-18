using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Edit-and-reprocess for a source that has already been extracted: apply the field
/// edits, tear down knowledge derived solely from this source, and requeue extraction.
/// Null fields keep their current values.
/// </summary>
public record ReprocessSourceCommand(
    Guid SourceId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string? Title = null,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null);
