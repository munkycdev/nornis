using Nornis.Application.Knowledge;

namespace Nornis.Application.Models;

public class LoremasterAnswer
{
    public required string AnswerText { get; init; }
    public required IReadOnlyList<Citation> Citations { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required IReadOnlyList<string> Caveats { get; init; }
}
