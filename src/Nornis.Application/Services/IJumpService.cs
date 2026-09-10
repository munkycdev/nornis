using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

/// <summary>
/// "Type a name, go there." Searches everything in a world a member might want to jump to,
/// grouped by kind, each group already filtered to the reader's visibility by the service
/// that owns that kind. This composes; it decides nothing about who may see what.
/// </summary>
public interface IJumpService
{
    Task<AppResult<JumpResult>> JumpAsync(JumpQuery query, CancellationToken ct);
}
