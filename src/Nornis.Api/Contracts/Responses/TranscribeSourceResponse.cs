namespace Nornis.Api.Contracts.Responses;

/// <param name="Markdown">
/// The transcription, already persisted as the source's body. Empty means the pages held no
/// readable handwriting — a result to show, not an error to retry.
/// </param>
public record TranscribeSourceResponse(string Markdown);
