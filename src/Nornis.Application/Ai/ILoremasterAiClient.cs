namespace Nornis.Application.Ai;

public interface ILoremasterAiClient
{
    Task<LoremasterAiResponse> AskAsync(LoremasterAiRequest request, CancellationToken ct);
}
