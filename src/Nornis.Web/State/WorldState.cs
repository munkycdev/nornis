using Nornis.Web.ApiClient;

namespace Nornis.Web.State;

/// <summary>
/// Per-circuit state for the active world. Everything in Nornis is world-scoped, so
/// components read the current world here and re-render on <see cref="Changed"/>. Loading is
/// lazy and idempotent via <see cref="EnsureLoadedAsync"/>.
/// </summary>
public class WorldState
{
    private readonly NornisApiClient _api;
    private readonly ViewAsState _viewAs;
    private bool _loaded;
    private Task? _loadingTask;

    // One continuity fetch per world: the sidebar ring, Home card, and World Memory page
    // all share this task instead of each firing their own request.
    private Guid? _continuityWorldId;
    private Task? _continuityTask;

    public WorldState(NornisApiClient api, ViewAsState viewAs)
    {
        _api = api;
        _viewAs = viewAs;
    }

    public IReadOnlyList<WorldSummary> Worlds { get; private set; } = [];
    public WorldSummary? Current { get; private set; }

    /// <summary>True while the GM is previewing the world as a player sees it.</summary>
    public bool ViewingAsPlayer => _viewAs.ViewingAsPlayer;

    /// <summary>
    /// The role every UI gate should read instead of <c>Current.MyRole</c>: the real role,
    /// downgraded to Player while view-as-player is active. The API applies the same
    /// downgrade server-side, so data and chrome stay consistent.
    /// </summary>
    public string? EffectiveRole => ViewingAsPlayer ? "Player" : Current?.MyRole;

    /// <summary>
    /// Enters or leaves player view. Only meaningful for GMs (the UI offers it to no one
    /// else). Leaving re-ensures the GM-only continuity assessment, which is skipped while
    /// the view is active.
    /// </summary>
    public void SetViewingAsPlayer(bool active)
    {
        if (_viewAs.ViewingAsPlayer == active)
        {
            return;
        }

        _viewAs.ViewingAsPlayer = active;
        Changed?.Invoke();

        if (!active && Continuity is null)
        {
            _ = LoadContinuityAsync();
        }
    }

    /// <summary>
    /// AI continuity assessment for <see cref="Current"/>, loaded on selection. Null for
    /// non-GM members (the endpoint is GM-only) — the UI hides the score ring then.
    /// </summary>
    public ContinuityAssessment? Continuity { get; private set; }

    /// <summary>Set when the continuity assessment could not be loaded.</summary>
    public ApiError? ContinuityError { get; private set; }

    /// <summary>True while a continuity fetch for the current world is in flight.</summary>
    public bool ContinuityLoading => _continuityTask is { IsCompleted: false };

    /// <summary>Set when the world list could not be loaded (e.g. the API is unreachable).</summary>
    public ApiError? LoadError { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Loads worlds once per circuit and selects the first as current. Concurrent callers
    /// (multiple components initializing at once) share a single load rather than each firing one.
    /// </summary>
    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return Task.CompletedTask;
        }

        return _loadingTask ??= ReloadAsync(ct);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var result = await _api.GetWorldsAsync(ct);
        _loaded = true;

        if (!result.IsSuccess)
        {
            LoadError = result.Error;
            Worlds = [];
            Current = null;
            Changed?.Invoke();
            return;
        }

        LoadError = null;
        Worlds = result.Value!;
        // Keep the current selection if it still exists, otherwise fall back to the first.
        Current = Worlds.FirstOrDefault(c => c.Id == Current?.Id) ?? Worlds.FirstOrDefault();
        Changed?.Invoke();

        await EnsureContinuityLoadedAsync(ct);
    }

    public void Select(Guid worldId)
    {
        var match = Worlds.FirstOrDefault(c => c.Id == worldId);
        if (match is not null && match.Id != Current?.Id)
        {
            Current = match;
            // Player view is a per-world peek; switching worlds returns to the real role.
            _viewAs.ViewingAsPlayer = false;
            Continuity = null;
            ContinuityError = null;
            Changed?.Invoke();
            _ = LoadContinuityAsync();
        }
    }

    /// <summary>
    /// Replaces the cached assessment with a fresher copy (e.g. after Home runs an assessment
    /// or dismisses a finding), then notifies so the sidebar ring stays in sync. Also marks the
    /// cache satisfied so a later <see cref="EnsureContinuityLoadedAsync"/> doesn't refetch.
    /// </summary>
    public void SetContinuity(ContinuityAssessment? assessment)
    {
        Continuity = assessment;
        ContinuityError = null;
        _continuityWorldId = Current?.Id;
        _continuityTask = Task.CompletedTask;
        Changed?.Invoke();
    }

    /// <summary>
    /// Returns the continuity load already running or completed for the current world, or
    /// starts one. Components awaiting the score share a single request per world.
    /// </summary>
    public Task EnsureContinuityLoadedAsync(CancellationToken ct = default)
    {
        if (_continuityWorldId == Current?.Id && _continuityTask is not null)
        {
            return _continuityTask;
        }

        return LoadContinuityAsync(ct);
    }

    /// <summary>Force-loads the AI continuity assessment for the current world, then notifies.</summary>
    public Task LoadContinuityAsync(CancellationToken ct = default)
    {
        _continuityWorldId = Current?.Id;
        _continuityTask = LoadContinuityCoreAsync(ct);
        return _continuityTask;
    }

    private async Task LoadContinuityCoreAsync(CancellationToken ct)
    {
        // The endpoint is GM-only — skip the guaranteed 403 for other roles, including a
        // GM currently viewing as player (the view-as header would make the server refuse).
        if (Current is null || !string.Equals(EffectiveRole, "GM", StringComparison.OrdinalIgnoreCase))
        {
            Continuity = null;
            ContinuityError = null;
            Changed?.Invoke();
            return;
        }

        var result = await _api.GetContinuityAssessmentAsync(Current.Id, ct);
        Continuity = result.IsSuccess ? result.Value : null;
        ContinuityError = result.IsSuccess ? null : result.Error;
        Changed?.Invoke();
    }
}
