namespace Nornis.Application.Ai;

public interface IArtifactSummaryAiClient
{
    Task<ArtifactSummaryAiResponse> SummarizeAsync(AiPromptRequest request, CancellationToken ct);
}

public class ArtifactSummaryAiResponse
{
    public required string Summary { get; init; }

    public required AiUsage Usage { get; init; }
}
