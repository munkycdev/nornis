namespace Nornis.Application.Ai;

public interface IAiExtractionClient
{
    Task<AiExtractionResponse> ExtractAsync(ExtractionRequest request, CancellationToken ct);
}
