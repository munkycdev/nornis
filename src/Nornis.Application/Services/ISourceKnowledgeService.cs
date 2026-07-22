using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// Read side of a source's extraction output: what artifacts, facts, and relationships
/// this source contributed to the record, limited to what the reader may see.
/// </summary>
public interface ISourceKnowledgeService
{
    Task<AppResult<SourceKnowledge>> GetForSourceAsync(
        Guid worldId, Guid sourceId, Guid requestingUserId, WorldRole role, CancellationToken ct);
}
