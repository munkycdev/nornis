using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface IExtractionService
{
    Task<ExtractionOutcome> ProcessExtractionAsync(
        Guid sourceId,
        Guid worldId,
        CancellationToken ct);
}
