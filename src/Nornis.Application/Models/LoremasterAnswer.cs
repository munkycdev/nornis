using Nornis.Application.Knowledge;

namespace Nornis.Application.Models;

/// <summary>A finished answer: the text as the model wrote it, and what it cited.</summary>
public class LoremasterAnswer
{
    public required string AnswerText { get; init; }
    public required IReadOnlyList<Citation> Citations { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required IReadOnlyList<string> Caveats { get; init; }
}
