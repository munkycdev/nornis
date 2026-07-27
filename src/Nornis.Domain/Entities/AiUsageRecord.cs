using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class AiUsageRecord
{
    public Guid Id { get; set; }

    public Guid? WorldId { get; set; }

    public Guid? UserId { get; set; }

    public AiOperationType OperationType { get; set; }

    public string Model { get; set; } = string.Empty;

    public int InputTokens { get; set; }

    /// <summary>
    /// Portion of <see cref="InputTokens"/> the provider billed as a prompt-cache hit, when it
    /// reports one. Null means this call path does not capture it — deliberately distinct from
    /// zero, which means the provider reported no cache hit at all. Without that distinction
    /// there is no way to tell "the prompt-cache change did not work" from "we cannot see
    /// whether it worked", and the two call for opposite responses.
    ///
    /// Currently captured on the extraction path, which is the overwhelming majority of AI spend
    /// and the one whose prompt carries a large world-stable prefix worth caching.
    /// </summary>
    public int? CachedInputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int TotalTokens { get; set; }

    public decimal EstimatedCostUsd { get; set; }

    public Guid? SourceId { get; set; }

    public Guid? ReviewBatchId { get; set; }

    public int DurationMs { get; set; }

    public bool Succeeded { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
