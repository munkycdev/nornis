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
    /// <summary>Mirrors <c>HttpContextExtensions.ViewAsHeaderName</c> in the API.</summary>
    private const string ViewAsHeaderName = "X-Nornis-View-As";

    private readonly HttpClient _httpClient;
    private readonly State.ViewAsState _viewAs;
    private readonly State.ActivitySignal _activity;

    public NornisApiClient(HttpClient httpClient, State.ViewAsState viewAs, State.ActivitySignal activity)
    {
        _httpClient = httpClient;
        _viewAs = viewAs;
        _activity = activity;
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

    /// <summary>Creates a ready-made demo world from the template, optionally with the
    /// guided tutorial. The server generates the name and rate-limits per user.</summary>
    public Task<ApiResult<WorldSummary>> CreateDemoWorldAsync(bool tutorial, CancellationToken ct = default) =>
        PostAsync<CreateDemoWorldBody, WorldSummary>("/api/worlds/demo", new CreateDemoWorldBody(tutorial), ct);

    private sealed record CreateDemoWorldBody(bool Tutorial);

    // ------------------------------------------------------ Onboarding + tutorial --

    public Task<ApiResult<OnboardingStateDto>> GetOnboardingAsync(CancellationToken ct = default) =>
        GetAsync<OnboardingStateDto>("/api/onboarding", ct);

    public Task<ApiResult<bool>> MarkOnboardingPromptSeenAsync(CancellationToken ct = default) =>
        PostNoContentAsync<object?>("/api/onboarding/prompt-seen", null, ct);

    /// <summary>Permanently dismisses all tutorial UI for the current user.</summary>
    public Task<ApiResult<bool>> DismissTutorialAsync(CancellationToken ct = default) =>
        PostNoContentAsync<object?>("/api/onboarding/dismiss-tutorial", null, ct);

    public Task<ApiResult<TutorialChecklistDto>> GetTutorialAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<TutorialChecklistDto>($"/api/worlds/{worldId}/tutorial", ct);

    public Task<ApiResult<TutorialChecklistDto>> ReportTutorialStepAsync(Guid worldId, string stepKey, CancellationToken ct = default) =>
        PostAsync<object?, TutorialChecklistDto>($"/api/worlds/{worldId}/tutorial/steps/{stepKey}", null, ct);

    public Task<ApiResult<TutorialSessionSixDto>> GetTutorialSessionSixAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<TutorialSessionSixDto>($"/api/worlds/{worldId}/tutorial/session-six", ct);

    public Task<ApiResult<WorldSummary>> UpdateWorldAsync(Guid worldId, UpdateWorldRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateWorldRequest, WorldSummary>($"/api/worlds/{worldId}", request, ct);

    /// <summary>Permanently deletes a world and everything in it. GM-only; the server
    /// re-verifies that <paramref name="confirmName"/> matches the world's exact name.</summary>
    public Task<ApiResult<bool>> DeleteWorldAsync(Guid worldId, string confirmName, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}{Query(("confirmName", confirmName))}", ct);

    /// <summary>GM-only: packages the selected data types into a single zip and returns a
    /// short-lived download URL for it. Category names are <c>WorldExportCategory</c> values.</summary>
    public Task<ApiResult<WorldExportDto>> ExportWorldAsync(Guid worldId, IReadOnlyList<string> categories, CancellationToken ct = default) =>
        PostAsync<ExportWorldRequest, WorldExportDto>($"/api/worlds/{worldId}/export", new ExportWorldRequest(categories), ct);

    // -------------------------------------------------------------------- Members --

    public Task<ApiResult<IReadOnlyList<WorldMember>>> GetMembersAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<WorldMember>>($"/api/worlds/{worldId}/members", ct);

    public Task<ApiResult<WorldMember>> GetMyMembershipAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<WorldMember>($"/api/worlds/{worldId}/members/me", ct);

    /// <summary>Sets the caller's own display name in this world; empty clears it.</summary>
    public Task<ApiResult<WorldMember>> UpdateMyDisplayNameAsync(Guid worldId, string? displayName, CancellationToken ct = default) =>
        PutAsync<UpdateMyMemberBody, WorldMember>($"/api/worlds/{worldId}/members/me", new UpdateMyMemberBody(displayName), ct);

    private sealed record UpdateMyMemberBody(string? DisplayName);

    public Task<ApiResult<WorldMember>> AddMemberAsync(Guid worldId, AddMemberRequest request, CancellationToken ct = default) =>
        PostAsync<AddMemberRequest, WorldMember>($"/api/worlds/{worldId}/members", request, ct);

    /// <summary>User directory (id + username) for the add-member picker.</summary>
    public Task<ApiResult<IReadOnlyList<UserSummaryDto>>> GetUsersAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<UserSummaryDto>>("/api/users", ct);

    public Task<ApiResult<WorldMember>> UpdateMemberRoleAsync(Guid worldId, Guid userId, string role, CancellationToken ct = default) =>
        PutAsync<UpdateMemberRoleRequest, WorldMember>($"/api/worlds/{worldId}/members/{userId}", new UpdateMemberRoleRequest(role), ct);

    public Task<ApiResult<bool>> RemoveMemberAsync(Guid worldId, Guid userId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/members/{userId}", ct);

    // -------------------------------------------------------------------- Invites --

    /// <summary>Lists a world's invite links (GM-only).</summary>
    public Task<ApiResult<IReadOnlyList<WorldInvite>>> GetWorldInvitesAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<WorldInvite>>($"/api/worlds/{worldId}/invites", ct);

    public Task<ApiResult<WorldInvite>> CreateWorldInviteAsync(Guid worldId, CreateInviteRequest request, CancellationToken ct = default) =>
        PostAsync<CreateInviteRequest, WorldInvite>($"/api/worlds/{worldId}/invites", request, ct);

    public Task<ApiResult<bool>> RevokeWorldInviteAsync(Guid worldId, Guid inviteId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/invites/{inviteId}", ct);

    /// <summary>Describes an invite for the landing page. Not world-scoped — the caller may not be a member yet.</summary>
    public Task<ApiResult<InvitePreview>> PreviewInviteAsync(string code, CancellationToken ct = default) =>
        GetAsync<InvitePreview>($"/api/invites/{code}", ct);

    /// <summary>Redeems an invite, joining the caller to the world.</summary>
    public Task<ApiResult<AcceptInviteResult>> AcceptInviteAsync(string code, CancellationToken ct = default) =>
        PostAsync<object?, AcceptInviteResult>($"/api/invites/{code}/accept", null, ct);

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

    public Task<ApiResult<IReadOnlyList<CharacterDto>>> GetCharactersAsync(Guid worldId, bool mine = false, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CharacterDto>>($"/api/worlds/{worldId}/characters{(mine ? "?mine=true" : "")}", ct);

    public Task<ApiResult<CharacterDto>> CreateCharacterAsync(Guid worldId, CreateCharacterRequest request, CancellationToken ct = default) =>
        PostAsync<CreateCharacterRequest, CharacterDto>($"/api/worlds/{worldId}/characters", request, ct);

    public Task<ApiResult<CharacterDto>> UpdateCharacterAsync(Guid worldId, Guid characterId, UpdateCharacterRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateCharacterRequest, CharacterDto>($"/api/worlds/{worldId}/characters/{characterId}", request, ct);

    public Task<ApiResult<bool>> DeleteCharacterAsync(Guid worldId, Guid characterId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/characters/{characterId}", ct);

    /// <summary>Transfers ownership of an existing character to the calling member.</summary>
    public Task<ApiResult<CharacterDto>> ClaimCharacterAsync(Guid worldId, Guid characterId, CancellationToken ct = default) =>
        PostAsync<object?, CharacterDto>($"/api/worlds/{worldId}/characters/{characterId}/claim", null, ct);

    // -------------------------------------------------------------------- Sources --

    /// <summary>Lightweight activity counts for navigation badges.</summary>
    public Task<ApiResult<SourceActivity>> GetSourceActivityAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<SourceActivity>($"/api/worlds/{worldId}/sources/activity", ct);

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

    /// <summary>GM-only: reveals a GM-only source (and its attachments, e.g. a map image) to the party.</summary>
    public Task<ApiResult<SourceDetailDto>> RevealSourceAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        PostAsync<object?, SourceDetailDto>($"/api/worlds/{worldId}/sources/{sourceId}/reveal", null, ct);

    // Source attachments (handwritten page images, ink documents) — SAS upload handshake.

    public Task<ApiResult<SourceAttachmentUploadTicketDto>> RequestSourceAttachmentUploadAsync(
        Guid worldId, Guid sourceId, RequestSourceAttachmentUploadRequest request, CancellationToken ct = default) =>
        PostAsync<RequestSourceAttachmentUploadRequest, SourceAttachmentUploadTicketDto>(
            $"/api/worlds/{worldId}/sources/{sourceId}/attachments/request-upload", request, ct);

    public Task<ApiResult<SourceAttachmentDto>> ConfirmSourceAttachmentUploadAsync(
        Guid worldId, Guid sourceId, Guid attachmentId, CancellationToken ct = default) =>
        PostAsync<object?, SourceAttachmentDto>(
            $"/api/worlds/{worldId}/sources/{sourceId}/attachments/{attachmentId}/confirm", null, ct);

    public Task<ApiResult<IReadOnlyList<SourceAttachmentDto>>> GetSourceAttachmentsAsync(
        Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<SourceAttachmentDto>>($"/api/worlds/{worldId}/sources/{sourceId}/attachments", ct);

    public Task<ApiResult<bool>> DeleteSourceAttachmentAsync(
        Guid worldId, Guid sourceId, Guid attachmentId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/sources/{sourceId}/attachments/{attachmentId}", ct);

    /// <summary>Applies a partial update to a source. The server rejects edits once the source
    /// is Queued/Processing/Processed.</summary>
    public Task<ApiResult<SourceDetailDto>> UpdateSourceAsync(Guid worldId, Guid sourceId, UpdateSourceRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateSourceRequest, SourceDetailDto>($"/api/worlds/{worldId}/sources/{sourceId}", request, ct);

    /// <summary>Deletes a source. The server rejects deletes while Queued/Processing.</summary>
    public Task<ApiResult<bool>> DeleteSourceAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/sources/{sourceId}", ct);

    /// <summary>What reprocessing this source would delete, for the confirmation dialog.</summary>
    public Task<ApiResult<ReprocessPreviewDto>> GetReprocessPreviewAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<ReprocessPreviewDto>($"/api/worlds/{worldId}/sources/{sourceId}/reprocess-preview", ct);

    /// <summary>What this source's extraction contributed to the record, reader-visible only.</summary>
    public Task<ApiResult<SourceKnowledgeDto>> GetSourceKnowledgeAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<SourceKnowledgeDto>($"/api/worlds/{worldId}/sources/{sourceId}/knowledge", ct);

    /// <summary>GM-only: removes an incorrect fact from canon; the note becomes a GM-note record.</summary>
    public Task<ApiResult<bool>> RemoveFactAsync(Guid worldId, Guid factId, string note, CancellationToken ct = default) =>
        PostNoContentAsync($"/api/worlds/{worldId}/facts/{factId}/removal", new RemoveFactRequest(note), ct);

    /// <summary>The source's map image + visible pins. 404 (no_map) when no map is stored.</summary>
    public Task<ApiResult<MapViewDto>> GetSourceMapAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<MapViewDto>($"/api/worlds/{worldId}/sources/{sourceId}/map", ct);

    /// <summary>Pins a known Location on the source's map, at its centre. Source creator or GM only.</summary>
    public Task<ApiResult<MapPlacemarkDto>> CreatePlacemarkAsync(
        Guid worldId, Guid sourceId, Guid artifactId, CancellationToken ct = default) =>
        PostAsync<CreatePlacemarkBody, MapPlacemarkDto>(
            $"/api/worlds/{worldId}/sources/{sourceId}/map/placemarks", new CreatePlacemarkBody(artifactId), ct);

    private sealed record CreatePlacemarkBody(Guid ArtifactId);

    /// <summary>Moves a map pin to a new normalized position. Source creator or GM only.</summary>
    public Task<ApiResult<MapPlacemarkDto>> MovePlacemarkAsync(
        Guid worldId, Guid sourceId, Guid placemarkId, decimal x, decimal y, CancellationToken ct = default) =>
        PatchAsync<MovePlacemarkBody, MapPlacemarkDto>(
            $"/api/worlds/{worldId}/sources/{sourceId}/map/placemarks/{placemarkId}", new MovePlacemarkBody(x, y), ct);

    private sealed record MovePlacemarkBody(decimal X, decimal Y);

    /// <summary>Removes a map pin; the pinned Location artifact is untouched. Source creator or GM only.</summary>
    public Task<ApiResult<bool>> RemovePlacemarkAsync(Guid worldId, Guid sourceId, Guid placemarkId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/sources/{sourceId}/map/placemarks/{placemarkId}", ct);

    /// <summary>The Location artifacts this session is linked to, visible to the caller.</summary>
    public Task<ApiResult<IReadOnlyList<LinkedLocationDto>>> GetSourceLocationsAsync(Guid worldId, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<LinkedLocationDto>>($"/api/worlds/{worldId}/sources/{sourceId}/locations", ct);

    /// <summary>Links a session to a Location artifact (idempotent); returns the updated set.</summary>
    public Task<ApiResult<IReadOnlyList<LinkedLocationDto>>> LinkSourceLocationAsync(Guid worldId, Guid sourceId, Guid artifactId, CancellationToken ct = default) =>
        PostAsync<LinkLocationBody, IReadOnlyList<LinkedLocationDto>>($"/api/worlds/{worldId}/sources/{sourceId}/locations", new LinkLocationBody(artifactId), ct);

    /// <summary>Removes a session's link to a Location (any link — extractor- or user-authored).</summary>
    public Task<ApiResult<bool>> UnlinkSourceLocationAsync(Guid worldId, Guid sourceId, Guid artifactId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/sources/{sourceId}/locations/{artifactId}", ct);

    private sealed record LinkLocationBody(Guid ArtifactId);

    /// <summary>Applies edits, deletes knowledge derived solely from this source, and requeues extraction.</summary>
    public Task<ApiResult<SourceDetailDto>> ReprocessSourceAsync(Guid worldId, Guid sourceId, ReprocessSourceRequest request, CancellationToken ct = default) =>
        PostAsync<ReprocessSourceRequest, SourceDetailDto>($"/api/worlds/{worldId}/sources/{sourceId}/reprocess", request, ct);

    /// <summary>GM-only: the world's active timeline replay; Replay is null when none is running.</summary>
    public Task<ApiResult<ExtractionReplayStateDto>> GetExtractionReplayAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<ExtractionReplayStateDto>($"/api/worlds/{worldId}/replay", ct);

    /// <summary>GM-only: how many notes a replay starting at this source would walk.</summary>
    public Task<ApiResult<ExtractionReplayPreviewDto>> GetExtractionReplayPreviewAsync(Guid worldId, Guid startSourceId, CancellationToken ct = default) =>
        GetAsync<ExtractionReplayPreviewDto>($"/api/worlds/{worldId}/replay/preview?startSourceId={startSourceId}", ct);

    /// <summary>GM-only: starts a timeline replay from this source (one note at a time, review-paced).</summary>
    public Task<ApiResult<ExtractionReplayDto>> StartExtractionReplayAsync(Guid worldId, Guid startSourceId, CancellationToken ct = default) =>
        PostAsync<StartExtractionReplayRequest, ExtractionReplayDto>($"/api/worlds/{worldId}/replay", new StartExtractionReplayRequest(startSourceId), ct);

    /// <summary>GM-only: cancels the active timeline replay; the in-flight note finishes normally.</summary>
    public Task<ApiResult<bool>> CancelExtractionReplayAsync(Guid worldId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/replay", ct);

    // Campaign backlog import — GM-only; one non-terminal session per world.

    /// <summary>GM-only: opens a new backlog import. 409 when one is already running.</summary>
    public Task<ApiResult<ImportSessionDto>> CreateImportSessionAsync(Guid worldId, CancellationToken ct = default) =>
        PostAsync<object?, ImportSessionDto>($"/api/worlds/{worldId}/import-sessions", null, ct);

    /// <summary>GM-only: the world's import in progress. 404 when there is none.</summary>
    public Task<ApiResult<ImportSessionDto>> GetCurrentImportSessionAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<ImportSessionDto>($"/api/worlds/{worldId}/import-sessions/current", ct);

    /// <summary>GM-only: adds a note to the backlog, held as a draft source until its turn.</summary>
    public Task<ApiResult<ImportSessionDto>> AddImportNoteAsync(
        Guid worldId, Guid sessionId, AddImportNoteRequest request, CancellationToken ct = default) =>
        PostAsync<AddImportNoteRequest, ImportSessionDto>(
            $"/api/worlds/{worldId}/import-sessions/{sessionId}/items", request, ct);

    /// <summary>GM-only: reorders the not-yet-started notes; pass every one of their ids.</summary>
    public Task<ApiResult<ImportSessionDto>> ReorderImportItemsAsync(
        Guid worldId, Guid sessionId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default) =>
        PutAsync<ReorderImportItemsRequest, ImportSessionDto>(
            $"/api/worlds/{worldId}/import-sessions/{sessionId}/items/order",
            new ReorderImportItemsRequest(itemIds), ct);

    /// <summary>GM-only: sources in this world that could be staged, in story order.</summary>
    public Task<ApiResult<List<ImportCandidateDto>>> ListImportCandidatesAsync(
        Guid worldId, Guid sessionId, CancellationToken ct = default) =>
        GetAsync<List<ImportCandidateDto>>(
            $"/api/worlds/{worldId}/import-sessions/{sessionId}/candidates", ct);

    /// <summary>GM-only: stages sources the world already holds, appended in story order.</summary>
    public Task<ApiResult<ImportSessionDto>> AddExistingImportSourcesAsync(
        Guid worldId, Guid sessionId, IReadOnlyList<Guid> sourceIds, CancellationToken ct = default) =>
        PostAsync<AddExistingSourcesRequest, ImportSessionDto>(
            $"/api/worlds/{worldId}/import-sessions/{sessionId}/items/existing",
            new AddExistingSourcesRequest(sourceIds), ct);

    /// <summary>GM-only: drops a note from the run. The source itself is left untouched.</summary>
    public Task<ApiResult<ImportSessionDto>> RemoveImportItemAsync(
        Guid worldId, Guid sessionId, Guid itemId, CancellationToken ct = default) =>
        DeleteAsync<ImportSessionDto>($"/api/worlds/{worldId}/import-sessions/{sessionId}/items/{itemId}", ct);

    /// <summary>GM-only: drops a note from the run AND deletes it. Only ever valid for a note
    /// this import created — the server refuses it for a source that existed beforehand.</summary>
    public Task<ApiResult<ImportSessionDto>> DeleteImportItemAsync(
        Guid worldId, Guid sessionId, Guid itemId, CancellationToken ct = default) =>
        DeleteAsync<ImportSessionDto>(
            $"/api/worlds/{worldId}/import-sessions/{sessionId}/items/{itemId}?deleteNote=true", ct);

    /// <summary>GM-only: begins the walk — the first note is sent for extraction.</summary>
    public Task<ApiResult<ImportSessionDto>> StartImportSessionAsync(
        Guid worldId, Guid sessionId, CancellationToken ct = default) =>
        PostAsync<object?, ImportSessionDto>($"/api/worlds/{worldId}/import-sessions/{sessionId}/start", null, ct);

    /// <summary>GM-only: moves to the next note, optionally skipping the current one.
    /// <paramref name="expectedItemId"/> is the note the screen was showing — the API refuses
    /// rather than acting on a note the user never saw.</summary>
    public Task<ApiResult<ImportSessionDto>> AdvanceImportSessionAsync(
        Guid worldId, Guid sessionId, bool skipCurrent = false, Guid? expectedItemId = null,
        CancellationToken ct = default) =>
        PostAsync<AdvanceImportSessionRequest, ImportSessionDto>(
            $"/api/worlds/{worldId}/import-sessions/{sessionId}/advance",
            new AdvanceImportSessionRequest(skipCurrent, expectedItemId), ct);

    /// <summary>GM-only: stops the import. Nothing is deleted.</summary>
    public Task<ApiResult<ImportSessionDto>> AbandonImportSessionAsync(
        Guid worldId, Guid sessionId, CancellationToken ct = default) =>
        PostAsync<object?, ImportSessionDto>($"/api/worlds/{worldId}/import-sessions/{sessionId}/abandon", null, ct);

    // ------------------------------------------------------------------ Public --
    // Anonymous read-only endpoints (/w/{slug} pages). Requests go out tokenless on
    // anonymous circuits — BearerTokenHandler no-ops without a token.

    public Task<ApiResult<PublicWorldDto>> GetPublicWorldAsync(string slug, CancellationToken ct = default) =>
        GetAsync<PublicWorldDto>($"/api/public/worlds/{Uri.EscapeDataString(slug)}", ct);

    public Task<ApiResult<IReadOnlyList<ArtifactListItem>>> GetPublicArtifactsAsync(string slug, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ArtifactListItem>>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/artifacts", ct);

    public Task<ApiResult<ArtifactDetailDto>> GetPublicArtifactAsync(string slug, Guid artifactId, CancellationToken ct = default) =>
        GetAsync<ArtifactDetailDto>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/artifacts/{artifactId}", ct);

    public Task<ApiResult<ArtifactGraphDto>> GetPublicArtifactGraphAsync(string slug, CancellationToken ct = default) =>
        GetAsync<ArtifactGraphDto>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/artifacts/graph", ct);

    public Task<ApiResult<StorylineTimelineDto>> GetPublicTimelineAsync(string slug, CancellationToken ct = default) =>
        GetAsync<StorylineTimelineDto>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/timeline", ct);

    /// <summary>The public journey: the world's auto-picked map and the party-visible trail over it.</summary>
    public Task<ApiResult<JourneyDto>> GetPublicJourneyAsync(string slug, CancellationToken ct = default) =>
        GetAsync<JourneyDto>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/journey", ct);

    public Task<ApiResult<IReadOnlyList<SourceListItem>>> GetPublicSourcesAsync(string slug, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<SourceListItem>>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/sources", ct);

    public Task<ApiResult<SourceDetailDto>> GetPublicSourceAsync(string slug, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<SourceDetailDto>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/sources/{sourceId}", ct);

    /// <summary>What a public session contributed to the record (party-visible only).</summary>
    public Task<ApiResult<SourceKnowledgeDto>> GetPublicSourceKnowledgeAsync(string slug, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<SourceKnowledgeDto>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/sources/{sourceId}/knowledge", ct);

    /// <summary>The Location artifacts a public session is linked to.</summary>
    public Task<ApiResult<IReadOnlyList<LinkedLocationDto>>> GetPublicSourceLocationsAsync(string slug, Guid sourceId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<LinkedLocationDto>>($"/api/public/worlds/{Uri.EscapeDataString(slug)}/sources/{sourceId}/locations", ct);

    /// <summary>Anonymous single-shot Ask the Loremaster against a public world. The API gates it
    /// on the GM's monthly spend cap; failures (disabled, budget spent) come back as the error.</summary>
    public Task<ApiResult<AskAnswer>> AskPublicAsync(string slug, string question, CancellationToken ct = default) =>
        PostAsync<PublicAskRequest, AskAnswer>(
            $"/api/public/worlds/{Uri.EscapeDataString(slug)}/ask", new PublicAskRequest(question), ct);

    // ------------------------------------------------------------------ Library --

    public Task<ApiResult<LibraryUploadTicketDto>> RequestLibraryUploadAsync(
        Guid worldId, RequestLibraryUploadRequest request, CancellationToken ct = default) =>
        PostAsync<RequestLibraryUploadRequest, LibraryUploadTicketDto>($"/api/worlds/{worldId}/library/request-upload", request, ct);

    public Task<ApiResult<LibraryDocumentDto>> ConfirmLibraryUploadAsync(Guid worldId, Guid documentId, CancellationToken ct = default) =>
        PostAsync<object?, LibraryDocumentDto>($"/api/worlds/{worldId}/library/{documentId}/confirm", null, ct);

    public Task<ApiResult<IReadOnlyList<LibraryDocumentDto>>> GetLibraryAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<LibraryDocumentDto>>($"/api/worlds/{worldId}/library", ct);

    public Task<ApiResult<LibraryDocumentDto>> GetLibraryDocumentAsync(Guid worldId, Guid documentId, CancellationToken ct = default) =>
        GetAsync<LibraryDocumentDto>($"/api/worlds/{worldId}/library/{documentId}", ct);

    public Task<ApiResult<LibraryDownloadDto>> GetLibraryDownloadAsync(Guid worldId, Guid documentId, CancellationToken ct = default) =>
        GetAsync<LibraryDownloadDto>($"/api/worlds/{worldId}/library/{documentId}/download", ct);

    public Task<ApiResult<LibraryDocumentDto>> SetLibraryVisibilityAsync(
        Guid worldId, Guid documentId, string visibility, CancellationToken ct = default) =>
        PutAsync<SetLibraryVisibilityBody, LibraryDocumentDto>(
            $"/api/worlds/{worldId}/library/{documentId}/visibility", new SetLibraryVisibilityBody(visibility), ct);

    private sealed record SetLibraryVisibilityBody(string Visibility);

    public Task<ApiResult<bool>> DeleteLibraryDocumentAsync(Guid worldId, Guid documentId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/library/{documentId}", ct);

    public Task<ApiResult<LibraryDocumentDto>> ReindexLibraryDocumentAsync(Guid worldId, Guid documentId, CancellationToken ct = default) =>
        PostAsync<object?, LibraryDocumentDto>($"/api/worlds/{worldId}/library/{documentId}/reindex", null, ct);

    // ------------------------------------------------------------------ Knowledge --

    public Task<ApiResult<IReadOnlyList<ArtifactListItem>>> GetArtifactsAsync(
        Guid worldId, string? type = null, string? status = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ArtifactListItem>>(
            $"/api/worlds/{worldId}/artifacts{Query(("type", type), ("status", status))}", ct);

    /// <summary>Global artifact search, most relevant first. Backs the top-bar search bar.</summary>
    public Task<ApiResult<IReadOnlyList<ArtifactListItem>>> SearchArtifactsAsync(
        Guid worldId, string term, int limit = 10, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ArtifactListItem>>(
            $"/api/worlds/{worldId}/artifacts/search{Query(("q", term), ("limit", limit.ToString()))}", ct);

    public Task<ApiResult<ArtifactDetailDto>> GetArtifactAsync(Guid worldId, Guid artifactId, CancellationToken ct = default) =>
        GetAsync<ArtifactDetailDto>($"/api/worlds/{worldId}/artifacts/{artifactId}", ct);

    /// <summary>GM-only: what removing this artifact from canon would delete.</summary>
    public Task<ApiResult<ArtifactRemovalPreview>> GetArtifactRemovalPreviewAsync(Guid worldId, Guid artifactId, CancellationToken ct = default) =>
        GetAsync<ArtifactRemovalPreview>($"/api/worlds/{worldId}/artifacts/{artifactId}/removal-preview", ct);

    /// <summary>GM-only: removes an artifact from canon and the knowledge attached to it.</summary>
    public Task<ApiResult<bool>> RemoveArtifactAsync(Guid worldId, Guid artifactId, CancellationToken ct = default) =>
        DeleteAsync($"/api/worlds/{worldId}/artifacts/{artifactId}", ct);

    public Task<ApiResult<ArtifactGraphDto>> GetArtifactGraphAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<ArtifactGraphDto>($"/api/worlds/{worldId}/artifacts/graph", ct);

    /// <summary>GM-only: folds the duplicate into the target; the duplicate is archived.</summary>
    public Task<ApiResult<MergeResult>> MergeArtifactAsync(Guid worldId, Guid targetArtifactId, Guid duplicateArtifactId, CancellationToken ct = default) =>
        PostAsync<MergeArtifactBody, MergeResult>($"/api/worlds/{worldId}/artifacts/{targetArtifactId}/merge",
            new MergeArtifactBody(duplicateArtifactId), ct);

    private sealed record MergeArtifactBody(Guid SourceArtifactId);

    /// <summary>GM-only: renames an artifact.</summary>
    public Task<ApiResult<ArtifactListItem>> RenameArtifactAsync(Guid worldId, Guid artifactId, string name, CancellationToken ct = default) =>
        PutAsync<RenameArtifactBody, ArtifactListItem>($"/api/worlds/{worldId}/artifacts/{artifactId}/name",
            new RenameArtifactBody(name), ct);

    private sealed record RenameArtifactBody(string Name);

    /// <summary>GM-only: sets or clears a storyline's parent storyline (null clears).</summary>
    public Task<ApiResult<bool>> SetStorylineParentAsync(Guid worldId, Guid artifactId, Guid? parentArtifactId, CancellationToken ct = default) =>
        PutAsync<SetStorylineParentBody, bool>($"/api/worlds/{worldId}/artifacts/{artifactId}/parent",
            new SetStorylineParentBody(parentArtifactId), ct);

    private sealed record SetStorylineParentBody(Guid? ParentArtifactId);

    /// <summary>GM-only: replaces the set of campaigns a storyline is declared to belong to (empty clears).</summary>
    public Task<ApiResult<bool>> SetStorylineCampaignsAsync(Guid worldId, Guid artifactId, IReadOnlyList<Guid> campaignIds, CancellationToken ct = default) =>
        PutAsync<SetStorylineCampaignsBody, bool>($"/api/worlds/{worldId}/artifacts/{artifactId}/campaigns",
            new SetStorylineCampaignsBody(campaignIds), ct);

    private sealed record SetStorylineCampaignsBody(IReadOnlyList<Guid> CampaignIds);

    /// <summary>GM-only: sets an artifact's lifecycle status.</summary>
    public Task<ApiResult<ArtifactListItem>> SetArtifactStatusAsync(Guid worldId, Guid artifactId, string status, CancellationToken ct = default) =>
        PutAsync<SetArtifactStatusBody, ArtifactListItem>($"/api/worlds/{worldId}/artifacts/{artifactId}/status",
            new SetArtifactStatusBody(status), ct);

    private sealed record SetArtifactStatusBody(string Status);

    /// <summary>GM-only: promotes GM-only artifacts/facts/relationships to the party. A 422
    /// (the set is not reference-closed) is surfaced as <see cref="RevealOutcome.Applied"/> =
    /// false carrying the artifacts that must also be revealed — not as an error — so the UI can
    /// offer to include them and retry.</summary>
    public async Task<ApiResult<RevealOutcome>> RevealAsync(Guid worldId, RevealBody body, CancellationToken ct = default)
    {
        try
        {
            var response = await SendCoreAsync(
                HttpMethod.Post, $"/api/worlds/{worldId}/reveal", JsonContent.Create(body), ct);

            if (response.IsSuccessStatusCode)
            {
                var applied = await response.Content.ReadFromJsonAsync<RevealResponseDto>(ct);
                return applied is null
                    ? ApiResult<RevealOutcome>.Fail(new ApiError("empty_response", "The API returned an empty response."))
                    : ApiResult<RevealOutcome>.Ok(new RevealOutcome(
                        true, applied.BatchId, applied.RevealedArtifacts, applied.RevealedFacts,
                        applied.RevealedRelationships, applied.Corrections, []));
            }

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var notClosed = await response.Content.ReadFromJsonAsync<RevealNotClosedDto>(ct);
                return ApiResult<RevealOutcome>.Ok(new RevealOutcome(
                    false, null, 0, 0, 0, 0, notClosed?.MissingArtifactIds ?? []));
            }

            var failed = await ReadResultAsync<RevealOutcome>(response, ct);
            return ApiResult<RevealOutcome>.Fail(failed.Error!);
        }
        catch (Exception ex)
        {
            return ApiResult<RevealOutcome>.Fail(Unreachable(ex));
        }
    }

    /// <summary>GM-only: assess Active storylines and propose closures as review proposals.</summary>
    public Task<ApiResult<RetrospectiveResult>> RunStorylineRetrospectiveAsync(Guid worldId, CancellationToken ct = default) =>
        PostAsync<object?, RetrospectiveResult>($"/api/worlds/{worldId}/storylines/retrospective", null, ct);

    /// <summary>GM-only: queue the relationship backfill sweep over processed sources.</summary>
    public Task<ApiResult<BackfillQueueResult>> QueueRelationshipBackfillAsync(Guid worldId, CancellationToken ct = default) =>
        PostAsync<object?, BackfillQueueResult>($"/api/worlds/{worldId}/storylines/backfill-relationships", null, ct);

    public Task<ApiResult<IReadOnlyList<ArtifactListItem>>> GetStorylinesAsync(
        Guid worldId, string? status = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ArtifactListItem>>(
            $"/api/worlds/{worldId}/storylines{Query(("status", status))}", ct);

    /// <summary>Session-dated storyline lanes for the timeline view.</summary>
    public Task<ApiResult<StorylineTimelineDto>> GetStorylineTimelineAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<StorylineTimelineDto>($"/api/worlds/{worldId}/storylines/timeline", ct);

    /// <summary>
    /// The world's journey over one map: its pins and the dated sessions that visited them, in
    /// order. Omit <paramref name="mapSourceId"/> to auto-pick the richest map.
    /// </summary>
    public Task<ApiResult<JourneyDto>> GetJourneyAsync(Guid worldId, Guid? mapSourceId = null, CancellationToken ct = default) =>
        GetAsync<JourneyDto>($"/api/worlds/{worldId}/journey{Query(("mapSourceId", mapSourceId?.ToString()))}", ct);

    /// <summary>GM-only: the session wrap-up view — what advanced, went quiet, could nest.</summary>
    public Task<ApiResult<WrapUpDto>> GetWrapUpAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<WrapUpDto>($"/api/worlds/{worldId}/storylines/wrap-up", ct);

    /// <summary>GM-only: apply the wrap-up decisions in one call.</summary>
    public Task<ApiResult<WrapUpApplyResult>> ApplyWrapUpAsync(Guid worldId, WrapUpDecisionsBody body, CancellationToken ct = default) =>
        PostAsync<WrapUpDecisionsBody, WrapUpApplyResult>($"/api/worlds/{worldId}/storylines/wrap-up", body, ct);

    public Task<ApiResult<IReadOnlyList<CanonEntry>>> GetCanonAsync(
        Guid worldId, string? truthState = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CanonEntry>>(
            $"/api/worlds/{worldId}/canon{Query(("truthState", truthState))}", ct);

    public Task<ApiResult<ReviewQueue>> GetReviewQueueAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<ReviewQueue>($"/api/worlds/{worldId}/reviews/proposals", ct);

    // -------------------------------------------------------------- Notifications --

    /// <summary>The VAPID public key, and whether the server can send at all.</summary>
    public Task<ApiResult<PushConfig>> GetPushConfigAsync(CancellationToken ct = default) =>
        GetAsync<PushConfig>("/api/notifications/config", ct);

    /// <summary>Browsers this user has enabled notifications on.</summary>
    public Task<ApiResult<List<PushSubscriptionSummary>>> GetPushSubscriptionsAsync(CancellationToken ct = default) =>
        GetAsync<List<PushSubscriptionSummary>>("/api/notifications/subscriptions", ct);

    /// <summary>Registers this browser to receive notifications.</summary>
    public Task<ApiResult<PushSubscriptionSummary>> SavePushSubscriptionAsync(
        SavePushSubscriptionRequest request, CancellationToken ct = default) =>
        PostAsync<SavePushSubscriptionRequest, PushSubscriptionSummary>(
            "/api/notifications/subscriptions", request, ct);

    /// <summary>Forgets a browser, by the endpoint it identifies itself with.</summary>
    public Task<ApiResult<bool>> DeletePushSubscriptionAsync(string endpoint, CancellationToken ct = default) =>
        DeleteAsync($"/api/notifications/subscriptions?endpoint={Uri.EscapeDataString(endpoint)}", ct);

    // ------------------------------------------------------------------------ Ask --

    public Task<ApiResult<AskAnswer>> AskLoremasterAsync(Guid worldId, string question, string? conversationContext = null, CancellationToken ct = default) =>
        PostAsync<AskRequest, AskAnswer>($"/api/worlds/{worldId}/ask", new AskRequest(question, conversationContext), ct);

    public Task<ApiResult<IReadOnlyList<AskSuggestion>>> GetAskSuggestionsAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AskSuggestion>>($"/api/worlds/{worldId}/ask/suggestions", ct);

    // AI-assessed Continuity Health (GM-only endpoints).

    public Task<ApiResult<ContinuityAssessment>> GetContinuityAssessmentAsync(Guid worldId, CancellationToken ct = default) =>
        GetAsync<ContinuityAssessment>($"/api/worlds/{worldId}/health/assessment", ct);

    public Task<ApiResult<ContinuityAssessment>> RunContinuityAssessmentAsync(Guid worldId, CancellationToken ct = default) =>
        PostAsync<object?, ContinuityAssessment>($"/api/worlds/{worldId}/health/assess", null, ct);

    public Task<ApiResult<ContinuityFinding>> DismissFindingAsync(Guid worldId, Guid findingId, CancellationToken ct = default) =>
        PostAsync<object?, ContinuityFinding>($"/api/worlds/{worldId}/health/findings/{findingId}/dismiss", null, ct);

    public Task<ApiResult<DraftFixResult>> DraftFindingFixAsync(Guid worldId, Guid findingId, CancellationToken ct = default) =>
        PostAsync<object?, DraftFixResult>($"/api/worlds/{worldId}/health/findings/{findingId}/draft-fix", null, ct);

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

    /// <summary>
    /// Every world-scoped call goes through here so view-as-player is stamped uniformly:
    /// while the GM previews as a player, the server must resolve their membership as
    /// Player for reads and GM-gated calls alike (those fail closed with 403 — the UI
    /// hides them in that mode).
    /// </summary>
    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method, string uri, HttpContent? content, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        if (_viewAs.ViewingAsPlayer)
        {
            request.Headers.Add(ViewAsHeaderName, "Player");
        }

        var response = await _httpClient.SendAsync(request, ct);

        // Every write goes through here, so this is the one place that cannot forget to say the
        // sidebar's counts are stale. Announcing on any successful write rather than on a list
        // of interesting endpoints is deliberate: the list would rot the first time someone adds
        // an endpoint, and the cost of being wrong is one cheap GET that the nav de-duplicates.
        if (method != HttpMethod.Get && response.IsSuccessStatusCode)
        {
            _activity.Notify();
        }

        return response;
    }

    private async Task<ApiResult<T>> GetAsync<T>(string uri, CancellationToken ct)
    {
        try
        {
            var response = await SendCoreAsync(HttpMethod.Get, uri, null, ct);
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
            var response = await SendCoreAsync(HttpMethod.Post, uri, JsonContent.Create(body), ct);
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
            var response = await SendCoreAsync(HttpMethod.Put, uri, JsonContent.Create(body), ct);
            return await ReadResultAsync<TValue>(response, ct);
        }
        catch (Exception ex)
        {
            return ApiResult<TValue>.Fail(Unreachable(ex));
        }
    }

    private async Task<ApiResult<TValue>> PatchAsync<TBody, TValue>(string uri, TBody body, CancellationToken ct)
    {
        try
        {
            var response = await SendCoreAsync(HttpMethod.Patch, uri, JsonContent.Create(body), ct);
            return await ReadResultAsync<TValue>(response, ct);
        }
        catch (Exception ex)
        {
            return ApiResult<TValue>.Fail(Unreachable(ex));
        }
    }

    private async Task<ApiResult<bool>> PostNoContentAsync<TBody>(string uri, TBody body, CancellationToken ct)
    {
        try
        {
            var response = await SendCoreAsync(HttpMethod.Post, uri, JsonContent.Create(body), ct);
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

    private async Task<ApiResult<bool>> DeleteAsync(string uri, CancellationToken ct)
    {
        try
        {
            var response = await SendCoreAsync(HttpMethod.Delete, uri, null, ct);
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

    /// <summary>DELETE whose response body matters — the caller wants the updated resource back.</summary>
    private async Task<ApiResult<T>> DeleteAsync<T>(string uri, CancellationToken ct)
    {
        try
        {
            var response = await SendCoreAsync(HttpMethod.Delete, uri, null, ct);
            return await ReadResultAsync<T>(response, ct);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Fail(Unreachable(ex));
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
