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
    private bool _loaded;
    private Task? _loadingTask;

    public WorldState(NornisApiClient api)
    {
        _api = api;
    }

    public IReadOnlyList<WorldSummary> Worlds { get; private set; } = [];
    public WorldSummary? Current { get; private set; }

    /// <summary>Heuristic continuity-health score for <see cref="Current"/>, loaded on selection.</summary>
    public WorldHealth? Health { get; private set; }

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

        await LoadHealthAsync(ct);
    }

    public void Select(Guid worldId)
    {
        var match = Worlds.FirstOrDefault(c => c.Id == worldId);
        if (match is not null && match.Id != Current?.Id)
        {
            Current = match;
            Health = null;
            Changed?.Invoke();
            _ = LoadHealthAsync();
        }
    }

    /// <summary>Loads the continuity-health score for the current world, then notifies.</summary>
    public async Task LoadHealthAsync(CancellationToken ct = default)
    {
        if (Current is null)
        {
            Health = null;
            return;
        }

        var result = await _api.GetWorldHealthAsync(Current.Id, ct);
        Health = result.IsSuccess ? result.Value : null;
        Changed?.Invoke();
    }
}
