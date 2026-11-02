namespace Nornis.Application.Ai;

/// <summary>
/// Drafts or revises one Prep section (feature 29). The prompt is built in Application by
/// <c>AdventureDraftPrompt</c>; this seam is the transport. It is the one client that carries an
/// output ceiling, so the final chunk's <see cref="AiStreamChunk.CutOff"/> is meaningful here.
/// </summary>
public interface IAdventureDraftingAiClient
{
    /// <summary>
    /// Streams the section's body as the model writes it. The last chunk carries the usage and
    /// whether the ceiling cut the text off. Failures are thrown from the enumeration in the
    /// usual AI taxonomy, possibly after text.
    /// </summary>
    IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct);
}
