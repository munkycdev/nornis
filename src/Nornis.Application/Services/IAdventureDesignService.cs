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

    /// <summary>
    /// Applies one suggested edit from a saved answer, keeping a revision. 409
    /// <c>edit_no_longer_fits</c> when its passage isn't in the draft exactly once.
    /// </summary>
    Task<AppResult<AdventureDraftState>> ApplyEditAsync(ApplyDraftEditCommand command, CancellationToken ct);

    /// <summary>Puts a whole saved answer into the draft (Replace or Append), keeping a revision.</summary>
    Task<AppResult<AdventureDraftState>> PutInDraftAsync(PutInDraftCommand command, CancellationToken ct);

    /// <summary>
    /// Cuts the draft into sections at the plan's start lines (<see cref="Prep.AdventureDraftCutter"/>,
    /// the only code that does), checked so no word changes, in one unit of work. The draft stays.
    /// </summary>
    Task<AppResult<AdventureView>> SplitAsync(SplitDraftCommand command, CancellationToken ct);

    /// <summary>Joins the sections into the draft, keeping the draft before it as a revision.</summary>
    Task<AppResult<AdventureDraftState>> GatherAsync(GatherDraftCommand command, CancellationToken ct);

    Task<AppResult<AdventureView>> SetTableBriefAsync(SetTableBriefCommand command, CancellationToken ct);

    /// <summary>At most <see cref="AdventureDesignService.MaxSourceBooks"/>, each a book of this world's Library.</summary>
    Task<AppResult<AdventureView>> SetSourceBooksAsync(SetSourceBooksCommand command, CancellationToken ct);
}
