namespace Nornis.Application.Ai;

/// <summary>
/// Prep's text calls (feature 29): Talk it through, the draft plan and the cover brief. The
/// prompts are built in Application; this seam is the transport. It is the one client that
/// carries an output ceiling, so the final chunk's <see cref="AiStreamChunk.CutOff"/> is
/// meaningful here.
/// </summary>
public interface IAdventureDraftingAiClient
{
    /// <summary>
    /// Streams the answer as the model writes it. The last chunk carries the usage and
    /// whether the ceiling cut the text off. Failures are thrown from the enumeration in the
    /// usual AI taxonomy, possibly after text.
    /// </summary>
    IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct);
}
