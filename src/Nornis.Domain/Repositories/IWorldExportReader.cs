using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

/// <summary>
/// Read-side counterpart of <see cref="IWorldRepository.DeleteAsync"/>'s graph walk: loads
/// every row that belongs to a world, restricted to the selected export categories.
/// </summary>
public interface IWorldExportReader
{
    Task<WorldExportData> ReadAsync(
        Guid worldId,
        IReadOnlySet<WorldExportCategory> categories,
        CancellationToken cancellationToken = default);
}
