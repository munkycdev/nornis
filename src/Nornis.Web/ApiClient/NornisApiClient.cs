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

    public Task<ApiResult<CampaignSummary>> UpdateCampaignAsync(Guid campaignId, UpdateCampaignRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateCampaignRequest, CampaignSummary>($"/api/campaigns/{campaignId}", request, ct);

    // -------------------------------------------------------------------- Members --

    public Task<ApiResult<IReadOnlyList<CampaignMember>>> GetMembersAsync(Guid campaignId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CampaignMember>>($"/api/campaigns/{campaignId}/members", ct);

    public Task<ApiResult<CampaignMember>> AddMemberAsync(Guid campaignId, AddMemberRequest request, CancellationToken ct = default) =>
        PostAsync<AddMemberRequest, CampaignMember>($"/api/campaigns/{campaignId}/members", request, ct);

    public Task<ApiResult<CampaignMember>> UpdateMemberRoleAsync(Guid campaignId, Guid userId, string role, CancellationToken ct = default) =>
        PutAsync<UpdateMemberRoleRequest, CampaignMember>($"/api/campaigns/{campaignId}/members/{userId}", new UpdateMemberRoleRequest(role), ct);

    public Task<ApiResult<bool>> RemoveMemberAsync(Guid campaignId, Guid userId, CancellationToken ct = default) =>
        DeleteAsync($"/api/campaigns/{campaignId}/members/{userId}", ct);

    // -------------------------------------------------------------------- Sources --

    public Task<ApiResult<IReadOnlyList<SourceListItem>>> GetSourcesAsync(Guid campaignId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<SourceListItem>>($"/api/campaigns/{campaignId}/sources", ct);

    public Task<ApiResult<SourceDetailDto>> GetSourceAsync(Guid campaignId, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<SourceDetailDto>($"/api/campaigns/{campaignId}/sources/{sourceId}", ct);

    public Task<ApiResult<SourceDetailDto>> CreateSourceAsync(Guid campaignId, CreateSourceRequest request, CancellationToken ct = default) =>
        PostAsync<CreateSourceRequest, SourceDetailDto>($"/api/campaigns/{campaignId}/sources", request, ct);

    /// <summary>Marks a source Ready, which enqueues it for AI extraction.</summary>
    public Task<ApiResult<SourceDetailDto>> MarkSourceReadyAsync(Guid campaignId, Guid sourceId, CancellationToken ct = default) =>
        PostAsync<object?, SourceDetailDto>($"/api/campaigns/{campaignId}/sources/{sourceId}/ready", null, ct);

    // ------------------------------------------------------------------ Knowledge --

    public Task<ApiResult<IReadOnlyList<ArtifactListItem>>> GetArtifactsAsync(
        Guid campaignId, string? type = null, string? status = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ArtifactListItem>>(
            $"/api/campaigns/{campaignId}/artifacts{Query(("type", type), ("status", status))}", ct);

    public Task<ApiResult<ArtifactDetailDto>> GetArtifactAsync(Guid campaignId, Guid artifactId, CancellationToken ct = default) =>
        GetAsync<ArtifactDetailDto>($"/api/campaigns/{campaignId}/artifacts/{artifactId}", ct);

    public Task<ApiResult<IReadOnlyList<ArtifactListItem>>> GetStorylinesAsync(
        Guid campaignId, string? status = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ArtifactListItem>>(
            $"/api/campaigns/{campaignId}/storylines{Query(("status", status))}", ct);

    public Task<ApiResult<IReadOnlyList<CanonEntry>>> GetCanonAsync(
        Guid campaignId, string? truthState = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CanonEntry>>(
            $"/api/campaigns/{campaignId}/canon{Query(("truthState", truthState))}", ct);

    public Task<ApiResult<ReviewQueue>> GetReviewQueueAsync(Guid campaignId, CancellationToken ct = default) =>
        GetAsync<ReviewQueue>($"/api/campaigns/{campaignId}/reviews/proposals", ct);

    // ------------------------------------------------------------------------ Ask --

    public Task<ApiResult<AskAnswer>> AskLoremasterAsync(Guid campaignId, string question, string? conversationContext = null, CancellationToken ct = default) =>
        PostAsync<AskRequest, AskAnswer>($"/api/campaigns/{campaignId}/ask", new AskRequest(question, conversationContext), ct);

    public Task<ApiResult<IReadOnlyList<AskSuggestion>>> GetAskSuggestionsAsync(Guid campaignId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AskSuggestion>>($"/api/campaigns/{campaignId}/ask/suggestions", ct);

    public Task<ApiResult<CampaignHealth>> GetCampaignHealthAsync(Guid campaignId, CancellationToken ct = default) =>
        GetAsync<CampaignHealth>($"/api/campaigns/{campaignId}/health", ct);

    // AI-assessed Continuity Health (GM-only endpoints).

    public Task<ApiResult<ContinuityAssessment>> GetContinuityAssessmentAsync(Guid campaignId, CancellationToken ct = default) =>
        GetAsync<ContinuityAssessment>($"/api/campaigns/{campaignId}/health/assessment", ct);

    public Task<ApiResult<ContinuityAssessment>> RunContinuityAssessmentAsync(Guid campaignId, CancellationToken ct = default) =>
        PostAsync<object?, ContinuityAssessment>($"/api/campaigns/{campaignId}/health/assess", null, ct);

    public Task<ApiResult<ContinuityFinding>> DismissFindingAsync(Guid campaignId, Guid findingId, CancellationToken ct = default) =>
        PostAsync<object?, ContinuityFinding>($"/api/campaigns/{campaignId}/health/findings/{findingId}/dismiss", null, ct);

    // ----------------------------------------------------------------------- Costs --

    public Task<ApiResult<TimePeriodSummary>> GetCostSummaryAsync(Guid campaignId, CancellationToken ct = default) =>
        GetAsync<TimePeriodSummary>($"/api/campaigns/{campaignId}/costs/summary", ct);

    public Task<ApiResult<IReadOnlyList<OperationTypeCost>>> GetCostsByOperationAsync(
        Guid campaignId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<OperationTypeCost>>(
            $"/api/campaigns/{campaignId}/costs/by-operation{DateQuery(from, to)}", ct);

    public Task<ApiResult<IReadOnlyList<ModelCost>>> GetCostsByModelAsync(
        Guid campaignId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ModelCost>>(
            $"/api/campaigns/{campaignId}/costs/by-model{DateQuery(from, to)}", ct);

    public Task<ApiResult<IReadOnlyList<UserCost>>> GetCostsByUserAsync(
        Guid campaignId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<UserCost>>(
            $"/api/campaigns/{campaignId}/costs/by-user{DateQuery(from, to)}", ct);

    private static string DateQuery(DateTimeOffset? from, DateTimeOffset? to) =>
        Query(("startDate", from?.ToString("o")), ("endDate", to?.ToString("o")));

    public Task<ApiResult<ProposalActionResult>> AcceptProposalAsync(Guid campaignId, Guid proposalId, CancellationToken ct = default) =>
        PostAsync<object?, ProposalActionResult>($"/api/campaigns/{campaignId}/reviews/proposals/{proposalId}/accept", null, ct);

    public Task<ApiResult<ProposalActionResult>> RejectProposalAsync(Guid campaignId, Guid proposalId, CancellationToken ct = default) =>
        PostAsync<object?, ProposalActionResult>($"/api/campaigns/{campaignId}/reviews/proposals/{proposalId}/reject", null, ct);

    public Task<ApiResult<ProposalActionResult>> EditProposalAsync(Guid campaignId, Guid proposalId, string proposedValueJson, CancellationToken ct = default) =>
        PostAsync<EditProposalBody, ProposalActionResult>(
            $"/api/campaigns/{campaignId}/reviews/proposals/{proposalId}/edit",
            new EditProposalBody(proposedValueJson), ct);

    public Task<ApiResult<BatchOperationResult>> BatchAcceptProposalsAsync(
        Guid campaignId, IReadOnlyList<Guid> proposalIds, CancellationToken ct = default) =>
        PostAsync<BatchProposalBody, BatchOperationResult>(
            $"/api/campaigns/{campaignId}/reviews/proposals/batch-accept",
            new BatchProposalBody(proposalIds), ct);

    public Task<ApiResult<BatchOperationResult>> BatchRejectProposalsAsync(
        Guid campaignId, IReadOnlyList<Guid> proposalIds, CancellationToken ct = default) =>
        PostAsync<BatchProposalBody, BatchOperationResult>(
            $"/api/campaigns/{campaignId}/reviews/proposals/batch-reject",
            new BatchProposalBody(proposalIds), ct);

    private sealed record EditProposalBody(string ProposedValueJson);

    private sealed record BatchProposalBody(IReadOnlyList<Guid> ProposalIds);

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

    private async Task<ApiResult<TValue>> PutAsync<TBody, TValue>(string uri, TBody body, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(uri, body, ct);
            return await ReadResultAsync<TValue>(response, ct);
        }
        catch (Exception ex)
        {
            return ApiResult<TValue>.Fail(Unreachable(ex));
        }
    }

    private async Task<ApiResult<bool>> DeleteAsync(string uri, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(uri, ct);
            if (response.IsSuccessStatusCode)
            {
                return ApiResult<bool>.Ok(true);
            }

            var failed = await ReadResultAsync<bool>(response, ct);
            return ApiResult<bool>.Fail(failed.Error!);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Fail(Unreachable(ex));
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

    private static string Query(params (string Key, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}")
            .ToArray();
        return parts.Length == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private sealed record HealthResponse(string? Status);
}

public enum ApiHealthStatus
{
    Healthy,
    Unhealthy,
    Unreachable
}

public record ApiHealth(ApiHealthStatus Status, int? HttpStatusCode);
