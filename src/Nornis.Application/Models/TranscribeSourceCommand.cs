using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record TranscribeSourceCommand(
    Guid SourceId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole);

/// <param name="Markdown">
/// The transcription, persisted as the source's body. Empty when the pages held no
/// readable handwriting — a result the caller should show as "nothing found" rather than
/// as a failure, because retrying will not change it.
/// </param>
public record SourceTranscriptionResult(string Markdown);
