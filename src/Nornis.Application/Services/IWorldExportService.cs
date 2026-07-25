using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

/// <summary>
/// Packages a world's data into a single zip in blob storage and returns a short-lived
/// download URL. Separate from <see cref="IWorldService"/> because it depends on blob
/// storage, whose DI registration throws when unconfigured — resolve this only on the
/// export endpoint so the rest of the world surface stays up.
/// </summary>
public interface IWorldExportService
{
    Task<AppResult<WorldExportResult>> ExportAsync(ExportWorldCommand command, CancellationToken ct);
}
