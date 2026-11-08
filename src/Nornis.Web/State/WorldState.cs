using Microsoft.JSInterop;
using Nornis.Web.ApiClient;

namespace Nornis.Web.State;

/// <summary>
/// Per-circuit state for the active world. Everything in Nornis is world-scoped, so
/// components read the current world here and re-render on <see cref="Changed"/>. Loading is
/// lazy and idempotent via <see cref="EnsureLoadedAsync"/>; the initial selection is a second,
/// separate step (<see cref="EnsureSelectionRestoredAsync"/>) because the saved world lives in
/// localStorage, which needs JS interop — nothing world-scoped should render before it runs.
/// </summary>
public class WorldState
{
    /// <summary>localStorage key holding the last selected world id.</summary>
    public const string StorageKey = "nornis:world";

    private readonly NornisApiClient _api;
    private readonly ViewAsState _viewAs;
    private readonly IJSRuntime _js;
    private bool _loaded;
    private Task? _loadingTask;
    private Task? _restoreTask;

    // One continuity fetch per world: the sidebar ring, Home card, and World Memory page
    // all share this task instead of each firing their own request.
    private Guid? _continuityWorldId;
    private Task? _continuityTask;

    public WorldState(NornisApiClient api, ViewAsState viewAs, IJSRuntime js)
    {
        _api = api;
        _viewAs = viewAs;
        _js = js;
    }

    public IReadOnlyList<WorldSummary> Worlds { get; private set; } = [];
    public WorldSummary? Current { get; private set; }

    /// <summary>
    /// True once the initial world selection is resolved (saved world restored from
    /// localStorage, or the fallback picked). The layout holds the page body behind a
    /// loading screen until this flips — that's what prevents the startup flash of
    /// content for a world the user didn't have selected.
    /// </summary>
    public bool Ready { get; private set; }

    /// <summary>True while the GM is previewing the world as a player sees it.</summary>
    public bool ViewingAsPlayer => _viewAs.ViewingAsPlayer;

    /// <summary>
    /// The role every UI gate should read instead of <c>Current.MyRole</c>: the real role,
    /// downgraded to Player while view-as-player is active. The API applies the same
    /// downgrade server-side, so data and chrome stay consistent.
    /// </summary>
    public string? EffectiveRole => ViewingAsPlayer ? "Player" : Current?.MyRole;

    /// <summary>
    /// The one GM test. Roles arrive from the API as strings, so every gate was spelling out
    /// its own comparison — twenty-seven of them, in three spellings, one of which had quietly
    /// become case-insensitive while the rest were not. A gate that reads differently in one
    /// file than in twenty-six others is a gate nobody can audit at a glance.
    /// </summary>
    public bool IsGm => EffectiveRole == WorldRoles.Gm;

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

    /// <summary>Raised when a world's key art changed here, so the chrome can show the new picture.</summary>
    public event Action? HeroChanged;

    /// <summary>
    /// Each world's hero (feature 42), as last read, keyed on world and effective role: the API
    /// answers a GM viewing as a player with the player's picture, so the two are kept apart.
    /// </summary>
    private readonly Dictionary<(Guid World, string? Role), WorldHeroDto> _heroes = [];

    /// <summary>The current world's hero as last read, without a request; null when none is cached.</summary>
    public WorldHeroDto? CachedHero =>
        Current is { } world && _heroes.TryGetValue((world.Id, EffectiveRole), out var hero) ? hero : null;

    /// <summary>
    /// The current world's hero: the cached one unless <paramref name="refresh"/>, which Home asks
    /// for so its counts are current. Null when the read failed; the chrome then shows no picture.
    /// </summary>
    public async Task<WorldHeroDto?> GetHeroAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (Current is not { } world)
        {
            return null;
        }

        var key = (world.Id, EffectiveRole);
        if (!refresh && _heroes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = await _api.GetWorldHeroAsync(world.Id, ct);
        if (!result.IsSuccess)
        {
            return null;
        }

        var changed = !_heroes.TryGetValue(key, out var previous) || previous.Picture != result.Value!.Picture;
        _heroes[key] = result.Value!;
        if (changed && refresh)
        {
            HeroChanged?.Invoke();
        }

        return result.Value;
    }

    /// <summary>Forgets a world's hero after its key art changed here, and tells the chrome.</summary>
    public void ForgetHero(Guid worldId)
    {
        foreach (var key in _heroes.Keys.Where(k => k.World == worldId).ToList())
        {
            _heroes.Remove(key);
        }

        HeroChanged?.Invoke();
    }

    /// <summary>
    /// Loads the world list once per circuit. Concurrent callers (multiple components
    /// initializing at once) share a single load rather than each firing one. Deliberately
    /// selects nothing before <see cref="EnsureSelectionRestoredAsync"/> has run — an eager
    /// "first world" pick here would render, then get swapped for the saved world (a flash).
    /// </summary>
    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return Task.CompletedTask;
        }

        return _loadingTask ??= ReloadAsync(ct);
    }

    /// <summary>
    /// Completes the boot: loads the list, then selects the world saved in localStorage,
    /// falling back to the first. Flips <see cref="Ready"/> and notifies before the
    /// continuity fetch so the UI unblocks as soon as the selection is known. Idempotent —
    /// every layout entry point may call it. Must run where JS interop is available
    /// (OnAfterRender), never during prerender.
    /// </summary>
    public Task EnsureSelectionRestoredAsync() => _restoreTask ??= RestoreSelectionCoreAsync();

    private async Task RestoreSelectionCoreAsync()
    {
        await EnsureLoadedAsync();

        Guid? saved = null;
        try
        {
            var raw = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (Guid.TryParse(raw, out var id))
            {
                saved = id;
            }
        }
        catch
        {
            // Best-effort — unreadable storage falls back to the first world.
        }

        Current = (saved is { } s ? Worlds.FirstOrDefault(w => w.Id == s) : null)
            ?? Worlds.FirstOrDefault();
        Ready = true;
        Changed?.Invoke();
        WarmBibliotaph();

        await EnsureContinuityLoadedAsync();
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
        // Keep the current selection if it still exists, otherwise fall back to the first —
        // but never auto-select before the initial restore has decided the saved world.
        Current = Ready
            ? Worlds.FirstOrDefault(c => c.Id == Current?.Id) ?? Worlds.FirstOrDefault()
            : null;
        Changed?.Invoke();

        await EnsureContinuityLoadedAsync(ct);
    }

    /// <summary>
    /// Selects a world and remembers it for the next full load.
    ///
    /// <para>
    /// Persisting lives here rather than at the call site, which is where it used to live — in
    /// the nav menu's own wrapper. Three of the four callers therefore did not persist at all:
    /// accepting an invite, creating a first world, and finishing onboarding each selected the
    /// new world for the session and then handed the user back their previous world on the next
    /// full load. Selecting and remembering are one act.
    /// </para>
    /// </summary>
    /// <summary>Opening a world warms its Bibliotaph (feature 31), so the first question doesn't wait for it.</summary>
    private void WarmBibliotaph()
    {
        if (Current is { } world)
        {
            _ = _api.WarmBibliotaphAsync(world.Id);
        }
    }

    public async Task SelectAsync(Guid worldId)
    {
        var match = Worlds.FirstOrDefault(c => c.Id == worldId);
        if (match is null)
        {
            return;
        }

        if (match.Id != Current?.Id)
        {
            Current = match;
            // Player view is a per-world peek; switching worlds returns to the real role.
            _viewAs.ViewingAsPlayer = false;
            Continuity = null;
            ContinuityError = null;
            Changed?.Invoke();
            _ = LoadContinuityAsync();
            WarmBibliotaph();
        }

        // Written even when the selection did not change: a re-select is how a caller repairs a
        // saved value that is missing or stale, and an early return would swallow that.
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, worldId.ToString());
        }
        catch
        {
            // Best-effort persist; the selection still holds for this session.
        }
    }

    /// <summary>
    /// Opens the member's demo world: the one they run if they have it, otherwise a new one with
    /// the guided tour. Then selects it. One rule for "the demo world", so the welcome page's
    /// start link and the first-login prompt can't disagree about whether to make a second.
    /// </summary>
    public async Task<ApiResult<WorldSummary>> OpenDemoAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        var demo = Worlds.FirstOrDefault(w => w.IsDemo && w.MyRole == WorldRoles.Gm);
        if (demo is null)
        {
            var created = await _api.CreateDemoWorldAsync(tutorial: true, ct);
            if (!created.IsSuccess)
            {
                return created;
            }

            await ReloadAsync(ct);
            demo = Worlds.FirstOrDefault(w => w.Id == created.Value!.Id) ?? created.Value!;
        }

        await SelectAsync(demo.Id);
        return ApiResult<WorldSummary>.Ok(demo);
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
        if (Current is null || !IsGm)
        {
            Continuity = null;
            ContinuityError = null;
            Changed?.Invoke();
            return;
        }

        // Capture before the await: switching worlds mid-flight would otherwise let the
        // previous world's assessment land under the new world's name. The continuity score
        // is a number a GM acts on, so showing the wrong world's is worse than showing none.
        var worldId = Current.Id;
        var result = await _api.GetContinuityAssessmentAsync(worldId, ct);

        if (Current?.Id != worldId)
        {
            return;
        }

        Continuity = result.IsSuccess ? result.Value : null;
        ContinuityError = result.IsSuccess ? null : result.Error;
        Changed?.Invoke();
    }
}
