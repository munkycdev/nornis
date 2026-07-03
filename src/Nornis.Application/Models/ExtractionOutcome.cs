namespace Nornis.Application.Models;

public enum OutcomeType
{
    Success,
    Skipped,
    TransientFailure,
    NonTransientFailure
}

public class ExtractionOutcome
{
    public required OutcomeType Type { get; init; }
    public string? ErrorCategory { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? ReviewBatchId { get; init; }
    public int ProposalCount { get; init; }

    public static ExtractionOutcome Succeeded(Guid reviewBatchId, int proposalCount) =>
        new() { Type = OutcomeType.Success, ReviewBatchId = reviewBatchId, ProposalCount = proposalCount };

    public static ExtractionOutcome SkippedIdempotent(string reason) =>
        new() { Type = OutcomeType.Skipped, ErrorMessage = reason };

    public static ExtractionOutcome Transient(string category, string message) =>
        new() { Type = OutcomeType.TransientFailure, ErrorCategory = category, ErrorMessage = message };

    public static ExtractionOutcome NonTransient(string category, string message) =>
        new() { Type = OutcomeType.NonTransientFailure, ErrorCategory = category, ErrorMessage = message };
}
