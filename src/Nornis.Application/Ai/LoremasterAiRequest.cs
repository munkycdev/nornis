namespace Nornis.Application.Ai;

public class LoremasterAiRequest
{
    public required string SystemPrompt { get; init; }
    public required string UserMessage { get; init; }
    public required string Model { get; init; }
    public required int TimeoutSeconds { get; init; }
}
