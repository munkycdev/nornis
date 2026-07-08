using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ISuggestionService
{
    /// <summary>
    /// Returns exactly four ask suggestions built from the campaign's current state,
    /// scoped to what the requesting member is allowed to see.
    /// </summary>
    Task<IReadOnlyList<AskSuggestion>> GetSuggestionsAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        CancellationToken ct);
}
