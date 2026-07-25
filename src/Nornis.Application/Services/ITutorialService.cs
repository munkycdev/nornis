using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface ITutorialService
{
    Task<OnboardingState> GetOnboardingAsync(Guid userId, CancellationToken ct);

    /// <summary>Records that the first-login prompt was shown; idempotent, never resets.</summary>
    Task MarkPromptSeenAsync(Guid userId, CancellationToken ct);

    /// <summary>Permanently dismisses all tutorial UI for this user (Requirement 4.4).</summary>
    Task DismissTutorialAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// The tutorial checklist for a demo world: runs state-backed detectors for steps not
    /// yet cached, records fresh completions, returns every step with completion state.
    /// </summary>
    Task<AppResult<TutorialChecklist>> GetChecklistAsync(Guid worldId, Guid userId, CancellationToken ct);

    /// <summary>Marks a client-reported step complete and returns the updated checklist.</summary>
    Task<AppResult<TutorialChecklist>> ReportStepAsync(Guid worldId, Guid userId, string stepKey, CancellationToken ct);

    /// <summary>The held-back "Session 6" paste text, read from the template package.</summary>
    Task<AppResult<string>> GetSessionSixAsync(CancellationToken ct);
}
