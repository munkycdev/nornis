using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

/// <summary>
/// GM-only removal of a single incorrect fact from canon. The removal is recorded by a
/// synthesized GM note source, so the correction itself carries provenance.
/// </summary>
public interface IFactRemovalService
{
    Task<AppResult> RemoveAsync(RemoveFactCommand command, CancellationToken ct);
}
