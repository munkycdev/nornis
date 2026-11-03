using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

/// <summary>
/// Prep's design mode (feature 37): the Adventure's one working draft, its table brief and the
/// Library books it reads. GM-only, like the rest of Prep, with the role checked first.
/// </summary>
public interface IAdventureDesignService
{
    /// <summary>
    /// The GM's own edit, which keeps no revision (the editor backs up unsaved typing). 409
    /// <c>draft_changed</c> when the draft moved on since <c>BasedOnUpdatedAt</c>.
    /// </summary>
    Task<AppResult<AdventureDraftState>> SaveDraftAsync(SaveDraftCommand command, CancellationToken ct);

    /// <summary>Restores the draft as it was before the last change that kept a revision, and drops that revision.</summary>
    Task<AppResult<AdventureDraftState>> UndoDraftAsync(UndoDraftCommand command, CancellationToken ct);

    Task<AppResult<AdventureView>> SetTableBriefAsync(SetTableBriefCommand command, CancellationToken ct);

    /// <summary>At most <see cref="AdventureDesignService.MaxSourceBooks"/>, each a book of this world's Library.</summary>
    Task<AppResult<AdventureView>> SetSourceBooksAsync(SetSourceBooksCommand command, CancellationToken ct);
}
