using Nornis.Application.Errors;
using Nornis.Application.Models;

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
}
