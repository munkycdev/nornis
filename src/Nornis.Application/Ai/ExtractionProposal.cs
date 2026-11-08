namespace Nornis.Application.Ai;

public class ExtractionProposal
{
    public required string ChangeType { get; init; }
    public required string TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public required object ProposedValue { get; init; }
    public required string Rationale { get; init; }
    public decimal? Confidence { get; init; }

    /// <summary>Short verbatim excerpt from the source supporting this proposal.</summary>
    public string? Quote { get; init; }

    /// <summary>
    /// What the table will do with this (feature 43). Supporting when the model did not say: a
    /// model that is silent has not called the proposal incidental, so an endpoint that ignores
    /// the schema loses nothing to triage.
    /// </summary>
    public ProposalSignificance Significance { get; init; } = ProposalSignificance.Supporting;
}
