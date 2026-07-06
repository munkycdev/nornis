using Nornis.Web.ApiClient;

namespace Nornis.Web.State;

/// <summary>
/// Per-circuit state for the active campaign. Everything in Nornis is campaign-scoped, so
/// components read the current campaign here and re-render on <see cref="Changed"/>. Loading is
/// lazy and idempotent via <see cref="EnsureLoadedAsync"/>.
/// </summary>
public class CampaignState
{
    private readonly NornisApiClient _api;
    private bool _loaded;

    public CampaignState(NornisApiClient api)
    {
        _api = api;
    }

    public IReadOnlyList<CampaignSummary> Campaigns { get; private set; } = [];
    public CampaignSummary? Current { get; private set; }

    /// <summary>Set when the campaign list could not be loaded (e.g. the API is unreachable).</summary>
    public ApiError? LoadError { get; private set; }

    public event Action? Changed;

    /// <summary>Loads campaigns once per circuit and selects the first as current.</summary>
    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return;
        }

        await ReloadAsync(ct);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var result = await _api.GetCampaignsAsync(ct);
        _loaded = true;

        if (!result.IsSuccess)
        {
            LoadError = result.Error;
            Campaigns = [];
            Current = null;
            Changed?.Invoke();
            return;
        }

        LoadError = null;
        Campaigns = result.Value!;
        // Keep the current selection if it still exists, otherwise fall back to the first.
        Current = Campaigns.FirstOrDefault(c => c.Id == Current?.Id) ?? Campaigns.FirstOrDefault();
        Changed?.Invoke();
    }

    public void Select(Guid campaignId)
    {
        var match = Campaigns.FirstOrDefault(c => c.Id == campaignId);
        if (match is not null && match.Id != Current?.Id)
        {
            Current = match;
            Changed?.Invoke();
        }
    }
}
