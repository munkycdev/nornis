namespace Nornis.Application.Ai;

public class ExtractionProposal
{
    public required string ChangeType { get; init; }
    public required string TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public required object ProposedValue { get; init; }
    public required string Rationale { get; init; }
    public decimal? Confidence { get; init; }
}
