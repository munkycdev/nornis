namespace Nornis.Application.Ai;

public interface IAiExtractionClient
{
    Task<AiExtractionResponse> ExtractAsync(AiPromptRequest request, CancellationToken ct);
}
