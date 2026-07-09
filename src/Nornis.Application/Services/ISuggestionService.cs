using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ISuggestionService
{
    /// <summary>
    /// Returns exactly four ask suggestions built from the world's current state,
    /// scoped to what the requesting member is allowed to see.
    /// </summary>
    Task<IReadOnlyList<AskSuggestion>> GetSuggestionsAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        CancellationToken ct);
}
