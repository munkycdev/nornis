using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// Storylines are artifacts with <see cref="ArtifactType.Storyline"/>. This is a dedicated
/// read view over the same artifact data, matching the Storylines navigation item.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/storylines")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class StorylinesController : ControllerBase
{
    private readonly IArtifactService _artifactService;
    private readonly IStorylineRetrospectiveService _retrospectiveService;
    private readonly IRelationshipBackfillQueueService _backfillQueueService;
    private readonly IStorylineContinuityService _continuityService;
    private readonly IStorylineWrapUpService _wrapUpService;

    public StorylinesController(
        IArtifactService artifactService,
        IStorylineRetrospectiveService retrospectiveService,
        IRelationshipBackfillQueueService backfillQueueService,
        IStorylineContinuityService continuityService,
        IStorylineWrapUpService wrapUpService)
    {
        _artifactService = artifactService;
        _retrospectiveService = retrospectiveService;
        _backfillQueueService = backfillQueueService;
        _continuityService = continuityService;
        _wrapUpService = wrapUpService;
    }

    /// <summary>
    /// GM-only: the deterministic staleness signal — which Active storylines have gone quiet
    /// and which are unanchored. No AI, no cost.
    /// </summary>
    [HttpGet("continuity")]
    public async Task<IActionResult> GetContinuity(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (member.Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role",
                "Only GMs can view the storyline continuity signal."));
        }

        var result = await _continuityService.GetContinuityReportAsync(worldId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToContinuityResponse(result.Value!));
    }

    /// <summary>GM-only: the session wrap-up view — what advanced, went quiet, could nest.</summary>
    [HttpGet("wrap-up")]
    public async Task<IActionResult> GetWrapUp(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _wrapUpService.GetWrapUpAsync(worldId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToWrapUpResponse(result.Value!));
    }

    /// <summary>GM-only: apply the wrap-up decisions (closures, lineage accept/reject, parenting).</summary>
    [HttpPost("wrap-up")]
    public async Task<IActionResult> ApplyWrapUp(Guid worldId, [FromBody] WrapUpDecisionsRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var closures = new List<WrapUpClosure>();
        foreach (var closure in request.Closures ?? [])
        {
            if (!Enum.TryParse<ArtifactStatus>(closure.Status, ignoreCase: true, out var status))
            {
                return BadRequest(new ErrorResponse("invalid_artifact_status",
                    $"'{closure.Status}' is not a valid artifact status."));
            }
            closures.Add(new WrapUpClosure(closure.StorylineId, status));
        }

        var command = new WrapUpDecisionsCommand(
            worldId,
            user.Id,
            member.Role,
            closures,
            request.AcceptProposalIds ?? [],
            request.RejectProposalIds ?? [],
            (request.Parents ?? []).Select(p => new WrapUpParentAssignment(p.ChildStorylineId, p.ParentStorylineId)).ToList());

        var result = await _wrapUpService.ApplyAsync(command, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var value = result.Value!;
        return Ok(new WrapUpApplyResponse(value.Closed, value.Nested, value.Rejected, value.Parented, value.BatchId));
    }

    internal static StorylineContinuityResponse ToContinuityResponse(StorylineContinuityReport report) =>
        new(
            report.ActiveCount,
            report.StaleThresholdSessions,
            report.LatestSession is { } s ? new ContinuitySessionRefResponse(s.SourceId, s.Title, s.OccurredAt) : null,
            report.Quiet.Select(q => new QuietStorylineResponse(
                q.StorylineId, q.Name, q.Status, q.LastDevelopmentAt,
                q.SessionsSinceLastDevelopment, q.OpenQuestionCount, q.ParentStorylineId)).ToList(),
            report.Unanchored.Select(u => new UnanchoredStorylineResponse(
                u.StorylineId, u.Name, u.CreatedAt, u.OpenQuestionCount)).ToList());

    internal static WrapUpResponse ToWrapUpResponse(WrapUpView view) =>
        new(
            view.HasWork,
            view.LatestSession is { } s ? new ContinuitySessionRefResponse(s.SourceId, s.Title, s.OccurredAt) : null,
            view.Advanced.Select(a => new WrapUpAdvancedResponse(
                a.StorylineId, a.Name, a.Status, a.RecentDevelopmentCount, a.LastDevelopmentAt)).ToList(),
            view.GoneQuiet.Select(q => new QuietStorylineResponse(
                q.StorylineId, q.Name, q.Status, q.LastDevelopmentAt,
                q.SessionsSinceLastDevelopment, q.OpenQuestionCount, q.ParentStorylineId)).ToList(),
            view.CouldNest.Select(n => new WrapUpNestSuggestionResponse(
                n.ProposalId, n.ChildStorylineId, n.ChildName, n.ParentStorylineId, n.ParentName, n.Rationale, n.Confidence)).ToList(),
            view.UnparentedArcs.Select(u => new WrapUpUnparentedResponse(
                u.StorylineId, u.Name, u.Status, u.FirstDevelopmentAt)).ToList(),
            view.ParentOptions.Select(p => new WrapUpParentOptionResponse(p.StorylineId, p.Name, p.Status)).ToList());

    /// <summary>
    /// GM-only: assess every Active storyline against the record and propose closures
    /// as review proposals.
    /// </summary>
    [HttpPost("retrospective")]
    public async Task<IActionResult> RunRetrospective(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _retrospectiveService.RunAsync(worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var value = result.Value!;
        return Ok(new RetrospectiveResponse(value.AssessedCount, value.ProposedCount, value.ReviewBatchId));
    }

    /// <summary>
    /// GM-only: queue the relationship backfill sweep — one worker message per processed
    /// source not yet swept, each producing Advances/PartOf link proposals for review.
    /// Safe to re-run; already-swept sources are skipped.
    /// </summary>
    [HttpPost("backfill-relationships")]
    public async Task<IActionResult> QueueRelationshipBackfill(Guid worldId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();

        var result = await _backfillQueueService.QueueBackfillAsync(worldId, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var value = result.Value!;
        return Ok(new BackfillQueueResponse(value.QueuedCount, value.AlreadySweptCount, value.TotalEligible));
    }

    /// <summary>
    /// The storyline timeline: lanes of session-dated developments per storyline, the
    /// sessions that produced them, and explicit storyline-to-storyline links.
    /// </summary>
    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _artifactService.GetStorylineTimelineAsync(worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToTimelineResponse(result.Value!));
    }

    /// <summary>Shared with the public read-only endpoints.</summary>
    internal static StorylineTimelineResponse ToTimelineResponse(Application.Models.StorylineTimeline timeline) =>
        new(
            timeline.Sessions.Select(s => new TimelineSessionResponse(s.SourceId, s.Title, s.OccurredAt, s.StorylineCount)).ToList(),
            timeline.Lanes.Select(l => new TimelineLaneResponse(
                l.StorylineId, l.Name, l.Status,
                l.Points.Select(p => new TimelinePointResponse(
                    p.SourceId, p.OccurredAt,
                    p.Developments.Select(d => new TimelineDevelopmentResponse(d.Kind, d.Text, d.Quote, d.IsOpenQuestion)).ToList())).ToList(),
                l.ParentStorylineId, l.CampaignName)).ToList(),
            timeline.Links.Select(x => new TimelineLinkResponse(x.FromStorylineId, x.ToStorylineId, x.Type)).ToList());

    [HttpGet]
    public async Task<IActionResult> List(
        Guid worldId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        ArtifactStatus? statusFilter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ArtifactStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                return BadRequest(new ErrorResponse("invalid_artifact_status", $"'{status}' is not a valid artifact status."));
            }
            statusFilter = parsedStatus;
        }

        var query = new ArtifactListQuery(
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Type: ArtifactType.Storyline,
            Status: statusFilter);

        var result = await _artifactService.ListAsync(query, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var response = result.Value!.Select(ArtifactsController.ToListItemResponse).ToList();

        return Ok(response);
    }

    private IActionResult MapError(AppError error)
    {
        return error.StatusCode switch
        {
            400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
            403 => StatusCode(403, new ErrorResponse(error.Code, error.Message)),
            404 => NotFound(new ErrorResponse(error.Code, error.Message)),
            409 => Conflict(new ErrorResponse(error.Code, error.Message)),
            _ => StatusCode(error.StatusCode, new ErrorResponse(error.Code, error.Message))
        };
    }
}
