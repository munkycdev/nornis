using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

/// <summary>
/// Export to Nornis (feature 29, Requirement 8): a snapshot of the Adventure, or one Chapter of
/// it, filed as a new GM-only <c>AdventureExport</c> source and queued through the path every
/// source takes. What the world learns from it goes through review like anything else.
/// </summary>
public interface IAdventureExportService
{
    Task<AppResult<Source>> ExportAsync(ExportAdventureCommand command, CancellationToken ct);
}
