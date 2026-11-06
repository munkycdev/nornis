using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface IExtractionService
{
    Task<ExtractionOutcome> ProcessExtractionAsync(
        Guid sourceId,
        Guid worldId,
        CancellationToken ct);

    /// <summary>
    /// The queue's last delivery of this source's message has failed and nothing will retry it:
    /// fail the source visibly, with <paramref name="failureCategory"/>, rather than leave it
    /// looking mid-extraction. True when the source was failed; false when it had already moved
    /// on, or a batch shows the extraction landed.
    /// </summary>
    Task<bool> GiveUpAsync(Guid sourceId, string? failureCategory, CancellationToken ct);
}
