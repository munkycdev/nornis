using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IAiUsageRecordRepository
{
    Task<AiUsageRecord> CreateAsync(AiUsageRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiUsageRecord>> QueryAsync(
        Guid? campaignId = null,
        Guid? userId = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        AiOperationType? operationType = null,
        CancellationToken cancellationToken = default);
}
