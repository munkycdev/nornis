using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface ISourceReprocessService
{
    /// <summary>
    /// Computes what <see cref="ReprocessAsync"/> would delete, without changing anything.
    /// Same authorization and status rules as the reprocess itself.
    /// </summary>
    Task<AppResult<ReprocessPreview>> PreviewAsync(
        Guid sourceId, Guid worldId, Guid actingUserId, Domain.Enums.WorldRole actingUserRole, CancellationToken ct);

    /// <summary>
    /// Applies the edits, deletes knowledge derived solely from this source, deletes its
    /// review batches, and requeues extraction.
    /// </summary>
    Task<AppResult<Domain.Entities.Source>> ReprocessAsync(ReprocessSourceCommand command, CancellationToken ct);
}
