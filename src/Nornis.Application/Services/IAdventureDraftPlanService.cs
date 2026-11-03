using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

/// <summary>
/// The split plan for the design draft (feature 37): from the draft's headings with no AI, or
/// proposed by the Loremaster. Either way it is only start lines, titles and kinds, and nothing
/// is written; the GM edits the plan and the split applies it.
/// </summary>
public interface IAdventureDraftPlanService
{
    /// <summary>
    /// The plan, each row with its opening words. A Loremaster plan that fails, or that
    /// <see cref="Prep.AdventureDraftPlan.Validate"/> refuses, falls back to the headings plan
    /// with a note saying so.
    /// </summary>
    Task<AppResult<DraftPlanView>> PlanAsync(PlanDraftCommand command, CancellationToken ct);
}
