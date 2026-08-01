namespace Nornis.Application.Ai;

public interface ILoremasterAiClient
{
    Task<LoremasterAiResponse> AskAsync(AiPromptRequest request, CancellationToken ct);
}
