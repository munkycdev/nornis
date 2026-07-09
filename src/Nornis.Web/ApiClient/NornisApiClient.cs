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

    // ------------------------------------------------------------------ Worlds --

    public Task<ApiResult<IReadOnlyList<WorldSummary>>> GetWorldsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<WorldSummary>>("/api/worlds", ct);

    public Task<ApiResult<WorldSummary>> CreateWorldAsync(CreateWorldRequest request, CancellationToken ct = default) =>
        PostAsync<CreateWorldRequest, WorldSummary>("/api/worlds", request, ct);

    public Task<ApiResult<WorldSummary>> UpdateWorldAsync(Guid worldId, UpdateWorldRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateWorldRequest, WorldSummary>($"/api/worlds/{worldId}", request, ct);

    // -------------------------------------------------------------------- Members --

    public Task<ApiResult<IReadOnlyList<WorldMember>>> GetMembersAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<WorldMember>>($"/api/worlds/{worldId}/members", ct);

    public Task<ApiResult<WorldMember>> AddMemberAsync(Guid worldId, AddMemberRequest request, CancellationToken ct = default) =>
        PostAsync<AddMemberRequest, WorldMember>($"/api/worlds/{worldId}/members", request, ct);

    public Task<ApiResult<WorldMember>> UpdateMemberRoleAsync(Guid worldId, Guid userId, string role, CancellationToken ct = default) =>
        PutAsync<UpdateMemberRoleRequest, WorldMember>($"/api/worlds/{worldId}/members/{userId}", new UpdateMemberRoleRequest(role), ct);

    public Task<ApiResult<bool>> RemoveMemberAsync(Guid worldId, Guid userId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/members/{userId}", ct);

    // ------------------------------------------------------------------ Campaigns --

    public Task<ApiResult<IReadOnlyList<CampaignDto>>> GetCampaignsAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CampaignDto>>($"/api/worlds/{worldId}/campaigns", ct);

    public Task<ApiResult<CampaignDto>> CreateCampaignAsync(Guid worldId, CreateCampaignRequest request, CancellationToken ct = default) =>
        PostAsync<CreateCampaignRequest, CampaignDto>($"/api/worlds/{worldId}/campaigns", request, ct);

    public Task<ApiResult<CampaignDto>> UpdateCampaignAsync(Guid worldId, Guid campaignId, UpdateCampaignRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateCampaignRequest, CampaignDto>($"/api/worlds/{worldId}/campaigns/{campaignId}", request, ct);

    public Task<ApiResult<bool>> DeleteCampaignAsync(Guid worldId, Guid campaignId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/campaigns/{campaignId}", ct);

    /// <summary>Replaces the full set of characters assigned to a campaign.</summary>
    public Task<ApiResult<IReadOnlyList<CharacterDto>>> AssignCampaignCharactersAsync(
        Guid worldId, Guid campaignId, IReadOnlyCollection<Guid> characterIds, CancellationToken ct = default) =>
        PutAsync<AssignCampaignCharactersRequest, IReadOnlyList<CharacterDto>>(
            $"/api/worlds/{worldId}/campaigns/{campaignId}/characters",
            new AssignCampaignCharactersRequest(characterIds), ct);

    // ----------------------------------------------------------------- Characters --

    public Task<ApiResult<IReadOnlyList<CharacterDto>>> GetCharactersAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CharacterDto>>($"/api/worlds/{worldId}/characters", ct);

    public Task<ApiResult<CharacterDto>> CreateCharacterAsync(Guid worldId, CreateCharacterRequest request, CancellationToken ct = default) =>
        PostAsync<CreateCharacterRequest, CharacterDto>($"/api/worlds/{worldId}/characters", request, ct);

    public Task<ApiResult<CharacterDto>> UpdateCharacterAsync(Guid worldId, Guid characterId, UpdateCharacterRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateCharacterRequest, CharacterDto>($"/api/worlds/{worldId}/characters/{characterId}", request, ct);

    public Task<ApiResult<bool>> DeleteCharacterAsync(Guid worldId, Guid characterId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/characters/{characterId}", ct);

    // -------------------------------------------------------------------- Sources --

    /// <param name="campaignFilter">A campaign id, the literal "none" for unassigned sources, or null for all.</param>
    public Task<ApiResult<IReadOnlyList<SourceListItem>>> GetSourcesAsync(Guid worldId, string? campaignFilter = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<SourceListItem>>($"/api/worlds/{worldId}/sources{Query(("campaignId", campaignFilter))}", ct);

    public Task<ApiResult<SourceDetailDto>> GetSourceAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<SourceDetailDto>($"/api/worlds/{worldId}/sources/{sourceId}", ct);

    public Task<ApiResult<SourceDetailDto>> CreateSourceAsync(Guid worldId, CreateSourceRequest request, CancellationToken ct = default) =>
        PostAsync<CreateSourceRequest, SourceDetailDto>($"/api/worlds/{worldId}/sources", request, ct);

    /// <summary>Marks a source Ready, which enqueues it for AI extraction.</summary>
    public Task<ApiResult<SourceDetailDto>> MarkSourceReadyAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        PostAsync<object?, SourceDetailDto>($"/api/worlds/{worldId}/sources/{sourceId}/ready", null, ct);

    /// <summary>Applies a partial update to a source. The server rejects edits once the source
    /// is Queued/Processing/Processed.</summary>
    public Task<ApiResult<SourceDetailDto>> UpdateSourceAsync(Guid worldId, Guid sourceId, UpdateSourceRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateSourceRequest, SourceDetailDto>($"/api/worlds/{worldId}/sources/{sourceId}", request, ct);

    /// <summary>Deletes a source. The server rejects deletes while Queued/Processing.</summary>
    public Task<ApiResult<bool>> DeleteSourceAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/sources/{sourceId}", ct);

    // ------------------------------------------------------------------ Knowledge --

    public Task<ApiResult<IReadOnlyList<ArtifactListItem>>> GetArtifactsAsync(
        Guid worldId, string? type = null, string? status = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ArtifactListItem>>(
            $"/api/worlds/{worldId}/artifacts{Query(("type", type), ("status", status))}", ct);

    public Task<ApiResult<ArtifactDetailDto>> GetArtifactAsync(Guid worldId, Guid artifactId, CancellationToken ct = default) =>
        GetAsync<ArtifactDetailDto>($"/api/worlds/{worldId}/artifacts/{artifactId}", ct);

    public Task<ApiResult<IReadOnlyList<ArtifactListItem>>> GetStorylinesAsync(
        Guid worldId, string? status = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ArtifactListItem>>(
            $"/api/worlds/{worldId}/storylines{Query(("status", status))}", ct);

    public Task<ApiResult<IReadOnlyList<CanonEntry>>> GetCanonAsync(
        Guid worldId, string? truthState = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CanonEntry>>(
            $"/api/worlds/{worldId}/canon{Query(("truthState", truthState))}", ct);

    public Task<ApiResult<ReviewQueue>> GetReviewQueueAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<ReviewQueue>($"/api/worlds/{worldId}/reviews/proposals", ct);

    // ------------------------------------------------------------------------ Ask --

    public Task<ApiResult<AskAnswer>> AskLoremasterAsync(Guid worldId, string question, string? conversationContext = null, CancellationToken ct = default) =>
        PostAsync<AskRequest, AskAnswer>($"/api/worlds/{worldId}/ask", new AskRequest(question, conversationContext), ct);

    public Task<ApiResult<IReadOnlyList<AskSuggestion>>> GetAskSuggestionsAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AskSuggestion>>($"/api/worlds/{worldId}/ask/suggestions", ct);

    public Task<ApiResult<WorldHealth>> GetWorldHealthAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<WorldHealth>($"/api/worlds/{worldId}/health", ct);

    // AI-assessed Continuity Health (GM-only endpoints).

    public Task<ApiResult<ContinuityAssessment>> GetContinuityAssessmentAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<ContinuityAssessment>($"/api/worlds/{worldId}/health/assessment", ct);

    public Task<ApiResult<ContinuityAssessment>> RunContinuityAssessmentAsync(Guid worldId, CancellationToken ct = default) =>
        PostAsync<object?, ContinuityAssessment>($"/api/worlds/{worldId}/health/assess", null, ct);

    public Task<ApiResult<ContinuityFinding>> DismissFindingAsync(Guid worldId, Guid findingId, CancellationToken ct = default) =>
        PostAsync<object?, ContinuityFinding>($"/api/worlds/{worldId}/health/findings/{findingId}/dismiss", null, ct);

    // ----------------------------------------------------------------------- Costs --

    public Task<ApiResult<TimePeriodSummary>> GetCostSummaryAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<TimePeriodSummary>($"/api/worlds/{worldId}/costs/summary", ct);

    public Task<ApiResult<IReadOnlyList<OperationTypeCost>>> GetCostsByOperationAsync(
        Guid worldId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<OperationTypeCost>>(
            $"/api/worlds/{worldId}/costs/by-operation{DateQuery(from, to)}", ct);

    public Task<ApiResult<IReadOnlyList<ModelCost>>> GetCostsByModelAsync(
        Guid worldId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ModelCost>>(
            $"/api/worlds/{worldId}/costs/by-model{DateQuery(from, to)}", ct);

    public Task<ApiResult<IReadOnlyList<UserCost>>> GetCostsByUserAsync(
        Guid worldId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<UserCost>>(
            $"/api/worlds/{worldId}/costs/by-user{DateQuery(from, to)}", ct);

    private static string DateQuery(DateTimeOffset? from, DateTimeOffset? to) =>
        Query(("startDate", from?.ToString("o")), ("endDate", to?.ToString("o")));

    /// <summary>Cost totals across every world where the caller is GM. Not scoped to the
    /// current world and accepts no date range. Returns 403 for callers with no GM worlds
    /// only when the endpoint forbids access; callers hide the section on empty or failure.</summary>
    public Task<ApiResult<IReadOnlyList<WorldCost>>> GetCostsByWorldAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<WorldCost>>("/api/costs/by-world", ct);

    public Task<ApiResult<ProposalActionResult>> AcceptProposalAsync(Guid worldId, Guid proposalId, CancellationToken ct = default) =>
        PostAsync<object?, ProposalActionResult>($"/api/worlds/{worldId}/reviews/proposals/{proposalId}/accept", null, ct);

    public Task<ApiResult<ProposalActionResult>> RejectProposalAsync(Guid worldId, Guid proposalId, CancellationToken ct = default) =>
        PostAsync<object?, ProposalActionResult>($"/api/worlds/{worldId}/reviews/proposals/{proposalId}/reject", null, ct);

    public Task<ApiResult<ProposalActionResult>> EditProposalAsync(Guid worldId, Guid proposalId, string proposedValueJson, CancellationToken ct = default) =>
        PostAsync<EditProposalBody, ProposalActionResult>(
            $"/api/worlds/{worldId}/reviews/proposals/{proposalId}/edit",
            new EditProposalBody(proposedValueJson), ct);

    public Task<ApiResult<BatchOperationResult>> BatchAcceptProposalsAsync(
        Guid worldId, IReadOnlyList<Guid> proposalIds, CancellationToken ct = default) =>
        PostAsync<BatchProposalBody, BatchOperationResult>(
            $"/api/worlds/{worldId}/reviews/proposals/batch-accept",
            new BatchProposalBody(proposalIds), ct);

    public Task<ApiResult<BatchOperationResult>> BatchRejectProposalsAsync(
        Guid worldId, IReadOnlyList<Guid> proposalIds, CancellationToken ct = default) =>
        PostAsync<BatchProposalBody, BatchOperationResult>(
            $"/api/worlds/{worldId}/reviews/proposals/batch-reject",
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
