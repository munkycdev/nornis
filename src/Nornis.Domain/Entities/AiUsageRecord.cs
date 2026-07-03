using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class AiUsageRecord
{
    public Guid Id { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? UserId { get; set; }

    public AiOperationType OperationType { get; set; }

    public string Model { get; set; } = string.Empty;

    public int InputTokens { get; set; }

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
