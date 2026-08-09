namespace Nornis.Application.Ai;

public interface IDigestAiClient
{
    Task<DigestAiResponse> GenerateAsync(AiPromptRequest request, CancellationToken ct);
}

public class DigestAiResponse
{
    public required string DigestMarkdown { get; init; }

    public required AiUsage Usage { get; init; }
}
