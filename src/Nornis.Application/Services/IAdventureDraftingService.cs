using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// Prep's AI (feature 29): draft or revise one section, streamed. Everything that can refuse the
/// draft (role, a stale stamp, a held gate, the budget) does so before the stream exists, so it
/// reaches the caller as an ordinary error rather than an event. Once the stream exists it ends
/// in exactly one <see cref="DraftEvent.Completed"/> or <see cref="DraftEvent.Failed"/>, or the
/// caller stops reading. Only <see cref="DraftEvent.Completed"/> writes to the section.
/// </summary>
public interface IAdventureDraftingService
{
    Task<AppResult<IAsyncEnumerable<DraftEvent>>> DraftAsync(DraftSectionCommand command, CancellationToken ct);

    /// <summary>The month's Prep spend against its ceiling. GM-only, like the rest of Prep.</summary>
    Task<AppResult<PrepBudgetStatus>> GetBudgetAsync(Guid worldId, WorldRole role, CancellationToken ct);
}
