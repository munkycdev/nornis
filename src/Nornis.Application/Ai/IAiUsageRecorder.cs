using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>
/// The one writer for the AI usage ledger. Services record outcomes; the recorder owns
/// the cost math they used to re-implement.
/// </summary>
public interface IAiUsageRecorder
{
    /// <summary>
    /// Writes one ledger row. <paramref name="usage"/> null means the call failed
    /// before the provider reported usage; the row still lands, priced at zero, with
    /// <paramref name="fallbackModel"/> naming the model that was asked for.
    /// </summary>
    Task RecordAsync(
        Guid? worldId,
        Guid? userId,
        AiOperationType operationType,
        AiUsage? usage,
        bool succeeded,
        string? errorCode,
        Guid? sourceId = null,
        Guid? reviewBatchId = null,
        string? fallbackModel = null,
        CancellationToken ct = default);
}
