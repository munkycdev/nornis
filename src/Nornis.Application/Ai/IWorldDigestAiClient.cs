namespace Nornis.Application.Ai;

public interface IWorldDigestAiClient
{
    Task<WorldDigestAiResponse> GenerateAsync(AiPromptRequest request, CancellationToken ct);
}

public class WorldDigestAiResponse
{
    public required string DigestMarkdown { get; init; }

    public required AiUsage Usage { get; init; }
}
