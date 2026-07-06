using System.Net;
using System.Net.Http.Json;

namespace Nornis.Web.ApiClient;

/// <summary>
/// Typed client for nornis-api. Read/write methods return <see cref="ApiResult{T}"/> so callers
/// handle expected failures (validation, forbidden, unreachable) without try/catch. In local dev
/// the API's dev-auth bypass provisions the user, so no token is attached yet.
/// </summary>
public class NornisApiClient
{
    private readonly HttpClient _httpClient;

    public NornisApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Base address the client is configured to call (for display/diagnostics).</summary>
    public Uri? BaseAddress => _httpClient.BaseAddress;

    /// <summary>
    /// Probes the anonymous <c>/health</c> endpoint. Returns the reported status, or
    /// <c>Unreachable</c> if the API cannot be contacted — never throws to the caller.
    /// </summary>
    public async Task<ApiHealth> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", ct);
            if (!response.IsSuccessStatusCode)
            {
                return new ApiHealth(ApiHealthStatus.Unhealthy, (int)response.StatusCode);
            }

            var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(ct);
            var healthy = string.Equals(payload?.Status, "Healthy", StringComparison.OrdinalIgnoreCase);
            return new ApiHealth(
                healthy ? ApiHealthStatus.Healthy : ApiHealthStatus.Unhealthy,
                (int)response.StatusCode);
        }
        catch (Exception)
        {
            return new ApiHealth(ApiHealthStatus.Unreachable, null);
        }
    }

    // ------------------------------------------------------------------ Campaigns --

    public Task<ApiResult<IReadOnlyList<CampaignSummary>>> GetCampaignsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CampaignSummary>>("/api/campaigns", ct);

    public Task<ApiResult<CampaignSummary>> CreateCampaignAsync(CreateCampaignRequest request, CancellationToken ct = default) =>
        PostAsync<CreateCampaignRequest, CampaignSummary>("/api/campaigns", request, ct);

    // -------------------------------------------------------------------- Sources --

    public Task<ApiResult<IReadOnlyList<SourceListItem>>> GetSourcesAsync(Guid campaignId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<SourceListItem>>($"/api/campaigns/{campaignId}/sources", ct);

    public Task<ApiResult<SourceDetail>> GetSourceAsync(Guid campaignId, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<SourceDetail>($"/api/campaigns/{campaignId}/sources/{sourceId}", ct);

    public Task<ApiResult<SourceDetail>> CreateSourceAsync(Guid campaignId, CreateSourceRequest request, CancellationToken ct = default) =>
        PostAsync<CreateSourceRequest, SourceDetail>($"/api/campaigns/{campaignId}/sources", request, ct);

    /// <summary>Marks a source Ready, which enqueues it for AI extraction.</summary>
    public Task<ApiResult<SourceDetail>> MarkSourceReadyAsync(Guid campaignId, Guid sourceId, CancellationToken ct = default) =>
        PostAsync<object?, SourceDetail>($"/api/campaigns/{campaignId}/sources/{sourceId}/ready", null, ct);

    // -------------------------------------------------------------------- Plumbing --

    private async Task<ApiResult<T>> GetAsync<T>(string uri, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(uri, ct);
            return await ReadResultAsync<T>(response, ct);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Fail(Unreachable(ex));
        }
    }

    private async Task<ApiResult<TValue>> PostAsync<TBody, TValue>(string uri, TBody body, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(uri, body, ct);
            return await ReadResultAsync<TValue>(response, ct);
        }
        catch (Exception ex)
        {
            return ApiResult<TValue>.Fail(Unreachable(ex));
        }
    }

    private static async Task<ApiResult<T>> ReadResultAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return ApiResult<T>.Ok(default!);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(ct);
            return value is null
                ? ApiResult<T>.Fail(new ApiError("empty_response", "The API returned an empty response."))
                : ApiResult<T>.Ok(value);
        }

        ApiError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiError>(ct);
        }
        catch
        {
            // Non-JSON error body — fall through to a generic message.
        }

        return ApiResult<T>.Fail(error ?? new ApiError(
            "http_" + (int)response.StatusCode,
            $"The API responded with {(int)response.StatusCode} {response.ReasonPhrase}."));
    }

    private ApiError Unreachable(Exception ex) =>
        new("unreachable", $"Could not reach nornis-api at {BaseAddress}. {ex.Message}");

    private sealed record HealthResponse(string? Status);
}

public enum ApiHealthStatus
{
    Healthy,
    Unhealthy,
    Unreachable
}

public record ApiHealth(ApiHealthStatus Status, int? HttpStatusCode);
