namespace Nornis.Application.Ai;

public class AiExtractionResponse
{
    public required IReadOnlyList<ExtractionProposal> Proposals { get; init; }
    public required AiUsage Usage { get; init; }
}
