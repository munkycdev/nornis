using Nornis.Web.ApiClient;

namespace Nornis.Web.State;

/// <summary>
/// Shares one <c>GET /api/onboarding</c> across the circuit.
///
/// Two components need this DTO and read different fields off it — <c>TutorialChecklist</c>
/// (mounted in MainLayout, so present on every authenticated page) reads
/// <see cref="OnboardingStateDto.TutorialDismissed"/>, and <c>OnboardingPrompt</c> (mounted on
/// the dashboard) reads <see cref="OnboardingStateDto.PromptSeen"/>. Each used to fetch it
/// independently, so the app's landing route issued the same request two or three times per
/// load depending on the render pass.
///
/// The shared-task pattern here matches <see cref="WorldState.EnsureLoadedAsync"/>: concurrent
/// callers await one in-flight request rather than each starting their own.
/// </summary>
public class OnboardingState(NornisApiClient api)
{
    private readonly NornisApiClient _api = api;
    private Task<ApiResult<OnboardingStateDto>>? _task;

    /// <summary>
    /// Returns the onboarding state, fetching it at most once per circuit unless invalidated.
    /// Failures are not cached — a transient error would otherwise stick for the whole circuit
    /// and permanently hide the tutorial.
    /// </summary>
    public async Task<ApiResult<OnboardingStateDto>> GetAsync(CancellationToken ct = default)
    {
        var task = _task ??= _api.GetOnboardingAsync(ct);
        var result = await task;

        if (!result.IsSuccess && ReferenceEquals(_task, task))
        {
            _task = null;
        }

        return result;
    }

    /// <summary>
    /// Drops the cached value so the next <see cref="GetAsync"/> refetches. Call after anything
    /// that changes onboarding state server-side (dismissing the tutorial, marking the prompt
    /// seen), or the components keep rendering from a stale copy for the rest of the circuit.
    /// </summary>
    public void Invalidate() => _task = null;
}
