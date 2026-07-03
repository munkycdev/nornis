namespace Nornis.Application.Ai;

public class LoremasterAiResponse
{
    public required string AnswerText { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required int TotalTokens { get; init; }
    public required int DurationMs { get; init; }
    public required string Model { get; init; }
}
