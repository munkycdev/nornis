namespace Nornis.Application.Ai;

public interface ILoremasterAiClient
{
    /// <summary>
    /// Streams the Loremaster's answer as the model writes it, [ref:…] markers and all. The
    /// last chunk carries the usage; see <see cref="AiStreamChunk"/> for what its absence means.
    /// Failures are thrown from the enumeration in the usual AI taxonomy, possibly after text.
    /// </summary>
    IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct);
}
